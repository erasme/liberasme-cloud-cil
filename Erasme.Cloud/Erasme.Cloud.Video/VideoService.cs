// VideoService.cs
// 
//  Get a converted video file to a Web compatible format
//
// Author(s):
//  Daniel Lacroix <dlacroix@erasme.org>
// 
// Copyright (c) 2013 Departement du Rhone
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
// 

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using Erasme.Http;
using Erasme.Json;
using Erasme.Cloud;
using Erasme.Cloud.Storage;

namespace Erasme.Cloud.Video
{
	public class VideoService: HttpHandler
	{		
		public StorageService Storage { get; private set; }
		string basePath;
		string temporaryDirectory;
		int cacheDuration;
		TaskFactory longRunningTaskFactory;
		object instanceLock = new object();
		Dictionary<string,Task> runningTasks = new Dictionary<string, Task>();
		
		public VideoService(string basepath, StorageService storage, string temporaryDirectory, int cacheDuration, TaskFactory longRunningTaskFactory)
		{
			basePath = basepath;
			Storage = storage;
			this.temporaryDirectory = temporaryDirectory;
			this.cacheDuration = cacheDuration;
			this.longRunningTaskFactory = longRunningTaskFactory;
			Storage.FileCreated += OnFileCreated;
			Storage.FileChanged += OnFileChanged;
			Storage.FileDeleted += OnFileDeleted;
			Storage.StorageDeleted += OnStorageDeleted;
		}

		static string BuildArguments(List<string> args)
		{
			string res = "";
			foreach(string arg in args) {
				string tmp = (string)arg.Clone();
				tmp = tmp.Replace("'", "\\'");
				if(res != "")
					res += " ";
				res += "'"+tmp+"'";
			}
			return res;
		}

		static string BuildArguments(string[] args)
		{
			string res = "";
			foreach(string arg in args) {
				string tmp = (string)arg.Clone();
				tmp = tmp.Replace("'", "\\'");
				if(res != "")
					res += " ";
				res += "'"+tmp+"'";
			}
			return res;
		}

		void BuildMp4(long storage, long file)
		{
			string mimetype;
			string filename;
			string filepath;
			long rev;
			try {
				Storage.GetDownloadFileInfo(storage, file, out mimetype, out filename, out rev);
				filepath = Storage.GetFullPath(storage, file);

				double rotation = Erasme.Cloud.Preview.ImageVideoPreview.GetVideoRotation(filepath);

				string videoFile = temporaryDirectory+"/"+Guid.NewGuid().ToString();

				List<string> args = new List<string>();
				args.Add("-loglevel"); args.Add("quiet");
				args.Add("-threads"); args.Add("1");
				args.Add("-i"); args.Add(filepath);
				args.Add("-f"); args.Add("mp4");
				args.Add("-vcodec"); args.Add("libx264");
				args.Add("-preset"); args.Add("slow");
				args.Add("-profile:v"); args.Add("baseline");
				args.Add("-map_metadata"); args.Add("-1");
				args.Add("-b"); args.Add("640k");
				args.Add("-ab"); args.Add("64k");
				args.Add("-ar"); args.Add("44100");
				args.Add("-ac"); args.Add("1");
				if(rotation == 90) {
					args.Add("-vf");
					args.Add("transpose=0,hflip");
				}
				else if(rotation == 180) {
					args.Add("-vf");
					args.Add("vflip,hflip");
				}
				else if(rotation == 270) {
					args.Add("-vf");
					args.Add("transpose=0,vflip");
				}
				args.Add(videoFile);

				ProcessStartInfo startInfo = new ProcessStartInfo("/usr/bin/ffmpegstatic", BuildArguments(args));

				using(Process process = new Process()) {
					process.StartInfo = startInfo;
					process.Start();
					process.WaitForExit();
				}
				
				if(File.Exists(videoFile)) {
					if(!Directory.Exists(basePath+"/"+storage))
						Directory.CreateDirectory(basePath+"/"+storage);
					if(!Directory.Exists(basePath+"/"+storage+"/mp4"))
						Directory.CreateDirectory(basePath+"/"+storage+"/mp4");
					File.Move(videoFile, basePath+"/"+storage+"/mp4/"+file);
				}
				// if original file was removed in the mean time, trash the MP4
				if(!File.Exists(filepath))
					File.Delete(basePath+"/"+storage+"/mp4/"+file);

			} finally {
				// remove the task
				lock(instanceLock) {
					if(runningTasks.ContainsKey(storage+":"+file+":mp4"))
						runningTasks.Remove(storage+":"+file+":mp4");
				}
			}
		}

		void BuildWebm(long storage, long file)
		{
			string mimetype;
			string filename;
			string filepath;
			long rev;
			try {
				Storage.GetDownloadFileInfo(storage, file, out mimetype, out filename, out rev);
				filepath = Storage.GetFullPath(storage, file);

				string videoFile = temporaryDirectory+"/"+Guid.NewGuid().ToString();

				double rotation = Erasme.Cloud.Preview.ImageVideoPreview.GetVideoRotation(filepath);
				List<string> args = new List<string>();
				args.Add("-loglevel"); args.Add("quiet");
				args.Add("-threads"); args.Add("1");
				args.Add("-i"); args.Add(filepath);
				args.Add("-f"); args.Add("webm");
				args.Add("-vcodec"); args.Add("libvpx");
				args.Add("-map_metadata"); args.Add("-1");
				args.Add("-acodec"); args.Add("libvorbis");
				args.Add("-b"); args.Add("640k");
				args.Add("-ab"); args.Add("64k");
				args.Add("-ar"); args.Add("44100");
				args.Add("-ac"); args.Add("1");
				if(rotation == 90) {
					args.Add("-vf");
					args.Add("transpose=0,hflip");
				}
				else if(rotation == 180) {
					args.Add("-vf");
					args.Add("vflip,hflip");
				}
				else if(rotation == 270) {
					args.Add("-vf");
					args.Add("transpose=0,vflip");
				}
				args.Add(videoFile);

				ProcessStartInfo startInfo = new ProcessStartInfo("/usr/bin/ffmpegstatic", BuildArguments(args));
				
				using(Process process = new Process()) {
					process.StartInfo = startInfo;
					process.Start();
					process.WaitForExit();
				}
				
				if(File.Exists(videoFile)) {
					if(!Directory.Exists(basePath+"/"+storage))
						Directory.CreateDirectory(basePath+"/"+storage);
					if(!Directory.Exists(basePath+"/"+storage+"/webm"))
						Directory.CreateDirectory(basePath+"/"+storage+"/webm");
					File.Move(videoFile, basePath+"/"+storage+"/webm/"+file);
				}
				// if original file was removed in the mean time, trash the WEBM
				if(!File.Exists(filepath))
					File.Delete(basePath+"/"+storage+"/webm/"+file);

			} finally {
				// remove the task
				lock(instanceLock) {
					if(runningTasks.ContainsKey(storage+":"+file+":webm"))
						runningTasks.Remove(storage+":"+file+":webm");
				}
			}
		}

		void OnFileCreated(long storage, long file) 
		{
			string mimetype;
			string filename;
			long rev;
			Storage.GetDownloadFileInfo(storage, file, out mimetype, out filename, out rev);
			
			if(mimetype.StartsWith("video/")) {
				lock(instanceLock) {
					if(!runningTasks.ContainsKey(storage+":"+file+":mp4") &&
					   !File.Exists(basePath+"/"+storage+"/mp4/"+file)) {
						Task task = longRunningTaskFactory.StartNew(delegate { BuildMp4(storage, file); });
						runningTasks[storage+":"+file+":mp4"] = task;
					}
					if(!runningTasks.ContainsKey(storage+":"+file+":webm") &&
					   !File.Exists(basePath+"/"+storage+"/webm/"+file)) {
						Task task = longRunningTaskFactory.StartNew(delegate { BuildWebm(storage, file); });
						runningTasks[storage+":"+file+":webm"] = task;
					}
				}
			}
		}
		
		void OnFileChanged(long storage, long file)
		{
			// TODO: rebuild the converted files
		}
		
		void OnFileDeleted(long storage, long file)
		{
			// delete the MP4 file
			if(File.Exists(basePath+"/"+storage+"/mp4/"+file))
				File.Delete(basePath+"/"+storage+"/mp4/"+file);
			// delete the WEBM file
			if(File.Exists(basePath+"/"+storage+"/webm/"+file))
				File.Delete(basePath+"/"+storage+"/webm/"+file);
		}
		
		void OnStorageDeleted(long storage)
		{
			if(Directory.Exists(basePath+"/"+storage))
				Directory.Delete(basePath+"/"+storage, true);
		}

		public override void ProcessRequest(HttpContext context)
		{
			string[] parts = context.Request.Path.Split(new char[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
			long id = 0;
			long id2 = 0;

			// GET /[storage]/[file] get video content
			if((context.Request.Method == "GET") && (parts.Length == 2) && long.TryParse(parts[0], out id) && long.TryParse(parts[1], out id2)) {
				string format = "mp4";
				if(context.Request.QueryString.ContainsKey("format"))
					format = context.Request.QueryString["format"];
				// if format is not set choose a format supported by the user agent
				else if(context.Request.Headers.ContainsKey("user-agent")) {
					if((context.Request.Headers["user-agent"].IndexOf("Firefox/") >= 0) ||
						(context.Request.Headers["user-agent"].IndexOf("Opera/") >= 0))
						format = "webm";
				}
				string mimetype = "video/mp4";
				if(format == "webm")
					mimetype = "video/webm";
				string fileName = basePath+"/"+id+"/"+format+"/"+id2;
				if(File.Exists(fileName)) {
					context.Response.StatusCode = 200;
					context.Response.Headers["content-type"] = mimetype;
					context.Response.Headers["cache-control"] = "max-age="+cacheDuration;
					context.Response.SupportRanges = true;
					context.Response.Content = new FileContent(fileName);
				}
				else {
					context.Response.StatusCode = 404;
					context.Response.Content = new StringContent("File not found\r\n");
				}
			}
			// GET /[storage]/[file]/info get video info
			else if((context.Request.Method == "GET") && (parts.Length == 3) && long.TryParse(parts[0], out id) && long.TryParse(parts[1], out id2) && (parts[2] == "info")) {
				JsonValue json = new JsonObject();
				json["storage"] = id;
				json["file"] = id2;
				string format = "mp4";
				if(context.Request.Headers.ContainsKey("user-agent")) {
					if((context.Request.Headers["user-agent"].IndexOf("Firefox/") >= 0) ||
					   (context.Request.Headers["user-agent"].IndexOf("Opera/") >= 0))
						format = "webm";
				}
				json["support"] = format;
				JsonValue status = new JsonObject();
				json["status"] = status;

				lock(instanceLock) {
					if(File.Exists(basePath+"/"+id+"/mp4/"+id2))
						status["mp4"] = "ready";
					else {
						if(runningTasks.ContainsKey(id+":"+id2+":mp4"))
							status["mp4"] = "building";
						else {
							Task task = longRunningTaskFactory.StartNew(delegate { BuildMp4(id, id2); });
							runningTasks[id+":"+id2+":mp4"] = task;
							status["mp4"] = "building";
						}
					}
					if(File.Exists(basePath+"/"+id+"/webm/"+id2))
						status["webm"] = "ready";
					else {
						if(runningTasks.ContainsKey(id+":"+id2+":webm"))
							status["webm"] = "building";
						else {
							Task task = longRunningTaskFactory.StartNew(delegate { BuildWebm(id, id2); });
							runningTasks[id+":"+id2+":webm"] = task;
							status["webm"] = "building";
						}
					}
				}
				context.Response.StatusCode = 200;
				context.Response.Content = new JsonContent(json);
			}
		}
	}
}
