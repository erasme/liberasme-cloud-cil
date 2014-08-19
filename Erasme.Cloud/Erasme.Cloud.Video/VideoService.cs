// VideoService.cs
// 
//  Get a converted video file to a Web compatible format
//
// Author(s):
//  Daniel Lacroix <dlacroix@erasme.org>
// 
// Copyright (c) 2013-2014 Departement du Rhone
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using Erasme.Http;
using Erasme.Json;
using Erasme.Cloud;
using Erasme.Cloud.Storage;
using Erasme.Cloud.Utils;

namespace Erasme.Cloud.Video
{
	public class VideoService: HttpHandler
	{		
		public StorageService Storage { get; private set; }
		string basePath;
		string temporaryDirectory;
		int cacheDuration;
		PriorityTaskScheduler longRunningTaskScheduler;
		object instanceLock = new object();
		Dictionary<string,LongTask> runningTasks = new Dictionary<string, LongTask>();
		
		public VideoService(string basepath, StorageService storage, string temporaryDirectory,
			int cacheDuration, PriorityTaskScheduler longRunningTaskScheduler)
		{
			basePath = basepath;
			Storage = storage;
			this.temporaryDirectory = temporaryDirectory;
			this.cacheDuration = cacheDuration;
			this.longRunningTaskScheduler = longRunningTaskScheduler;
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

		void BuildMp4(string storage, long file)
		{
			//Console.WriteLine("BuildMp4(" + storage + "," + file + ")");
			string mimetype;
			string filename;
			string filepath;
			long rev;
			try {
				Storage.GetDownloadFileInfo(storage, file, out mimetype, out filename, out rev);
				filepath = Storage.GetFullPath(storage, file);

				double rotation = Erasme.Cloud.Preview.ImageVideoPreview.GetVideoRotation(filepath);

				double width; double height;
				Erasme.Cloud.Preview.ImageVideoPreview.GetVideoSize(filepath, out width, out height);

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
				// variable depending on the quality expected
				int resizedHeight;
				// 720p
				if(height >= 720) {
					resizedHeight = 720;
					args.Add("-b:v"); args.Add("2560k");
				}
				// 480p
				else if(height >= 480) {
					resizedHeight = 480;
					args.Add("-b:v"); args.Add("1280k");
				}
				// 240p
				else {
					resizedHeight = 240;
					args.Add("-b:v"); args.Add("640k");
				}
				int resizedWidth = (int)(Math.Ceiling((((double)resizedHeight)*(width/height))/16)*16);
				args.Add("-s"); args.Add(resizedWidth+"x"+resizedHeight);

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
				//} catch(Exception e) {
				//Console.WriteLine(e.ToString());
			} finally {
				// remove the task
				lock(instanceLock) {
					if(runningTasks.ContainsKey(storage+":"+file.ToString()+":mp4"))
						runningTasks.Remove(storage+":"+file.ToString()+":mp4");
				}
			}
		}

		void BuildWebm(string storage, long file)
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
				double width; double height;
				Erasme.Cloud.Preview.ImageVideoPreview.GetVideoSize(filepath, out width, out height);

				List<string> args = new List<string>();
				args.Add("-loglevel"); args.Add("quiet");
				args.Add("-threads"); args.Add("1");
				args.Add("-i"); args.Add(filepath);
				args.Add("-f"); args.Add("webm");
				args.Add("-vcodec"); args.Add("libvpx");
				args.Add("-map_metadata"); args.Add("-1");
				args.Add("-acodec"); args.Add("libvorbis");
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

				// variable depending on the quality expected
				int resizedHeight;
				// 720p
				if(height >= 720) {
					resizedHeight = 720;
					args.Add("-b:v"); args.Add("2560k");
				}
				// 480p
				else if(height >= 480) {
					resizedHeight = 480;
					args.Add("-b:v"); args.Add("1280k");
				}
				// 240p
				else {
					resizedHeight = 240;
					args.Add("-b:v"); args.Add("640k");
				}
				int resizedWidth = (int)(Math.Ceiling((((double)resizedHeight)*(width/height))/16)*16);
				args.Add("-s"); args.Add(resizedWidth+"x"+resizedHeight);

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
					if(runningTasks.ContainsKey(storage+":"+file.ToString()+":webm"))
						runningTasks.Remove(storage+":"+file.ToString()+":webm");
				}
			}
		}

		void OnFileCreated(string storage, long file) 
		{
			string mimetype;
			string filename;
			long rev;
			Storage.GetDownloadFileInfo(storage, file, out mimetype, out filename, out rev);
			
			if(mimetype.StartsWith("video/")) {
				lock(instanceLock) {
					if(!runningTasks.ContainsKey(storage+":"+file+":mp4") &&
					   !File.Exists(basePath+"/"+storage+"/mp4/"+file)) {
						LongTask task = new LongTask(delegate {
							BuildMp4(storage, file);
						}, null, "Build MP4 for "+storage+":"+file);
						longRunningTaskScheduler.Start(task);
						runningTasks[storage+":"+file+":mp4"] = task;
					}
					if(!runningTasks.ContainsKey(storage+":"+file+":webm") &&
					   !File.Exists(basePath+"/"+storage+"/webm/"+file)) {
						LongTask task = new LongTask(delegate {
							BuildWebm(storage, file);
						}, null, "Build WEBM for "+storage+":"+file, LongTaskPriority.Low);
						longRunningTaskScheduler.Start(task);
						runningTasks[storage+":"+file+":webm"] = task;
					}
				}
			}
		}
		
		void OnFileChanged(string storage, long file)
		{
			// TODO: rebuild the converted files
		}
		
		void OnFileDeleted(string storage, long file)
		{
			// delete the MP4 file
			if(File.Exists(basePath+"/"+storage+"/mp4/"+file))
				File.Delete(basePath+"/"+storage+"/mp4/"+file);
			// delete the WEBM file
			if(File.Exists(basePath+"/"+storage+"/webm/"+file))
				File.Delete(basePath+"/"+storage+"/webm/"+file);
		}
		
		void OnStorageDeleted(string storage)
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
			if((context.Request.Method == "GET") && (parts.Length == 2) && long.TryParse(parts[1], out id2)) {
				string storage = parts[0];
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
				string fileName = basePath+"/"+storage+"/"+format+"/"+id2;
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
			else if((context.Request.Method == "GET") && (parts.Length == 3) && long.TryParse(parts[1], out id2) && (parts[2] == "info")) {
				string storage = parts[0];
				long file = id2;
				JsonValue json = new JsonObject();
				json["storage"] = storage;
				json["file"] = file;
				string format = "mp4";
				if(context.Request.Headers.ContainsKey("user-agent")) {
					if((context.Request.Headers["user-agent"].IndexOf("Firefox/") >= 0) ||
					   (context.Request.Headers["user-agent"].IndexOf("Opera/") >= 0))
						format = "webm";
				}
				json["support"] = format;
				JsonValue status = new JsonObject();
				json["status"] = status;

				//Console.WriteLine("video info storage: " + storage + ", file: " + file);

				if(File.Exists(basePath + "/" + storage + "/mp4/" + file.ToString()))
					status["mp4"] = "ready";
				else {
					lock(instanceLock) {
						if(runningTasks.ContainsKey(storage + ":" + file.ToString() + ":mp4")) {
							status["mp4"] = "building";
						}
						else {
							LongTask task = new LongTask(delegate {
								BuildMp4(storage, file);
							}, context.User, "Build MP4 for " + storage + ":" + file);
							longRunningTaskScheduler.Start(task);
							runningTasks[storage + ":" + file.ToString() + ":mp4"] = task;
							status["mp4"] = "building";
						}
					}
				}

				if(File.Exists(basePath+"/"+storage+"/webm/"+file.ToString()))
					status["webm"] = "ready";
				else {
					lock(instanceLock) {
						if(runningTasks.ContainsKey(storage + ":" + file.ToString() + ":webm"))
							status["webm"] = "building";
						else {
							LongTask task = new LongTask(delegate {
								BuildWebm(storage, file);
							}, context.User, "Build WEBM for " + storage + ":" + file, LongTaskPriority.Low);
							longRunningTaskScheduler.Start(task);
							runningTasks[storage + ":" + file.ToString() + ":webm"] = task;
							status["webm"] = "building";
						}
					}
				}
				context.Response.StatusCode = 200;
				context.Response.Content = new JsonContent(json);
				context.SendResponse();
			}
		}
	}
}
