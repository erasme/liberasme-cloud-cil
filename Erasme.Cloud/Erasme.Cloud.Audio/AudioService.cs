// AudioService.cs
// 
//  Get a converted audio file to a Web compatible format
//
// Author(s):
//  Daniel Lacroix <dlacroix@erasme.org>
// 
// Copyright (c) 2012-2013 Departement du Rhone
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

namespace Erasme.Cloud.Audio
{
	public class AudioService: HttpHandler
	{		
		public StorageService Storage { get; private set; }
		string basePath;
		string temporaryDirectory;
		int cacheDuration;
		PriorityTaskScheduler longRunningTaskScheduler;
		object instanceLock = new object();
		Dictionary<string,LongTask> runningTasks = new Dictionary<string, LongTask>();
		
		public AudioService(string basepath, StorageService storage, string temporaryDirectory,
			int cacheDuration, PriorityTaskScheduler longRunningTaskScheduler)
		{
			basePath = basepath;
			this.temporaryDirectory = temporaryDirectory;
			this.cacheDuration = cacheDuration;
			this.longRunningTaskScheduler = longRunningTaskScheduler;
			Storage = storage;
			Storage.FileCreated += OnFileCreated;
			Storage.FileChanged += OnFileChanged;
			Storage.FileDeleted += OnFileDeleted;
			Storage.StorageDeleted += OnStorageDeleted;
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

		void BuildMp3(string storage, long file)
		{
			string mimetype;
			string filename;
			string filepath;
			long rev;
			try {

				Storage.GetDownloadFileInfo(storage, file, out mimetype, out filename, out rev);
				filepath = Storage.GetFullPath(storage, file);

				string audioFile = temporaryDirectory+"/"+Guid.NewGuid().ToString();

				ProcessStartInfo startInfo = new ProcessStartInfo("/usr/bin/ffmpegstatic", BuildArguments(new string[] {
					"-loglevel", "quiet", "-threads", "1",
					"-i", filepath, "-map", "a",
					"-f", "mp3", "-ab", "64k", 
					"-ar", "44100", "-ac", "1",
					audioFile
				}));
				
				using(Process process = new Process()) {
					process.StartInfo = startInfo;
					process.Start();
					process.WaitForExit();
				}
				
				if(File.Exists(audioFile)) {
					if(!Directory.Exists(basePath + "/" + storage))
						Directory.CreateDirectory(basePath + "/" + storage);
					if(!Directory.Exists(basePath + "/" + storage + "/mp3"))
						Directory.CreateDirectory(basePath + "/" + storage + "/mp3");
					File.Move(audioFile, basePath + "/" + storage + "/mp3/" + file);
				}
				// if original file was removed in the mean time, trash the MP3
				if(!File.Exists(filepath))
					File.Delete(basePath + "/" + storage + "/mp3/" + file);
			
			} finally {
				// remove the task
				lock(instanceLock) {
					if(runningTasks.ContainsKey(storage + ":" + file + ":mp3"))
						runningTasks.Remove(storage + ":" + file + ":mp3");
				}
			}
		}

		void BuildOgg(string storage, long file)
		{
			string mimetype;
			string filename;
			string filepath;
			long rev;
			try {

				Storage.GetDownloadFileInfo(storage, file, out mimetype, out filename, out rev);
				filepath = Storage.GetFullPath(storage, file);

				string audioFile = temporaryDirectory+"/"+Guid.NewGuid().ToString();

				ProcessStartInfo startInfo = new ProcessStartInfo("/usr/bin/ffmpegstatic", BuildArguments(new string[]{
					"-loglevel", "quiet", "-threads", "1",
					"-i", filepath, "-map", "a",
					"-f", "ogg", "-ab", "64k", 
					"-ar", "44100", "-ac", "1", "-acodec", "libvorbis",
					audioFile
				}));
				
				using(Process process = new Process()) {
					process.StartInfo = startInfo;
					process.Start();
					process.WaitForExit();
				}
				
				if(File.Exists(audioFile)) {
					if(!Directory.Exists(basePath+"/"+storage))
						Directory.CreateDirectory(basePath+"/"+storage);
					if(!Directory.Exists(basePath+"/"+storage+"/ogg"))
						Directory.CreateDirectory(basePath+"/"+storage+"/ogg");
					File.Move(audioFile, basePath+"/"+storage+"/ogg/"+file);
				}
				// if original file was removed in the mean time, trash the OGG
				if(!File.Exists(filepath))
					File.Delete(basePath+"/"+storage+"/ogg/"+file);

			} finally {
				// remove the task
				lock(instanceLock) {
					if(runningTasks.ContainsKey(storage+":"+file+":ogg"))
						runningTasks.Remove(storage+":"+file+":ogg");
				}
			}
		}

		void OnFileCreated(string storage, long file) 
		{
			string mimetype;
			string filename;
			long rev;
			Storage.GetDownloadFileInfo(storage, file, out mimetype, out filename, out rev);
			
			if(mimetype.StartsWith("audio/")) {
				lock(instanceLock) {
					if(!runningTasks.ContainsKey(storage+":"+file+":mp3") &&
					   !File.Exists(basePath+"/"+storage+"/mp3/"+file)) {
						LongTask task = new LongTask(delegate {
							BuildMp3(storage, file);
						}, null, "Build MP3 "+storage+":"+file);
						longRunningTaskScheduler.Start(task);
						runningTasks[storage+":"+file+":mp3"] = task;
					}
					if(!runningTasks.ContainsKey(storage+":"+file+":ogg") &&
					   !File.Exists(basePath+"/"+storage+"/ogg/"+file)) {
						LongTask task = new LongTask(delegate {
							BuildOgg(storage, file); 
						}, null, "Build OGG "+storage+":"+file, LongTaskPriority.Low);

						longRunningTaskScheduler.Start(task);
						runningTasks[storage+":"+file+":ogg"] = task;
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
			// delete the MP3 file
			if(File.Exists(basePath+"/"+storage+"/mp3/"+file))
				File.Delete(basePath+"/"+storage+"/mp3/"+file);
			// delete the OGG file
			if(File.Exists(basePath+"/"+storage+"/ogg/"+file))
				File.Delete(basePath+"/"+storage+"/ogg/"+file);
		}
		
		void OnStorageDeleted(string storage)
		{
			if(Directory.Exists(basePath+"/"+storage))
				Directory.Delete(basePath+"/"+storage, true);
		}

		public override void ProcessRequest(HttpContext context)
		{
			string[] parts = context.Request.Path.Split(new char[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
			long file = 0;

			// GET /[storage]/[file] get audio content 
			if((context.Request.Method == "GET") && (parts.Length == 2) && long.TryParse(parts[1], out file)) {
				string storage = parts[0];
				string format = "mp3";
				if(context.Request.QueryString.ContainsKey("format"))
					format = context.Request.QueryString["format"];
				// if format is not set choose a format supported by the user agent
				else if(context.Request.Headers.ContainsKey("user-agent")) {
					if((context.Request.Headers["user-agent"].IndexOf("Firefox/") >= 0) ||
						(context.Request.Headers["user-agent"].IndexOf("Opera/") >= 0))
						format = "ogg";
				}
				string mimetype = "audio/mpeg";
				if(format == "ogg")
					mimetype = "audio/ogg";
				string fileName = basePath+"/"+storage+"/"+format+"/"+file;
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
			// GET /[storage]/[file]/info get audio info
			else if((context.Request.Method == "GET") && (parts.Length == 3) && long.TryParse(parts[1], out file) && (parts[2] == "info")) {
				string storage = parts[0];
				JsonValue json = new JsonObject();
				json["storage"] = storage;
				json["file"] = file;
				string format = "mp3";
				if(context.Request.Headers.ContainsKey("user-agent")) {
					if((context.Request.Headers["user-agent"].IndexOf("Firefox/") >= 0) ||
					   (context.Request.Headers["user-agent"].IndexOf("Opera/") >= 0))
						format = "ogg";
				}
				json["support"] = format;
				JsonValue status = new JsonObject();
				json["status"] = status;

				lock(instanceLock) {
					if(File.Exists(basePath+"/"+storage+"/mp3/"+file))
						status["mp3"] = "ready";
					else {
						if(runningTasks.ContainsKey(storage+":"+file+":mp3"))
							status["mp3"] = "building";
						else {
							LongTask task = new LongTask(delegate {
								BuildMp3(storage, file);
							}, null, "Build MP3 "+storage+":"+file);
							longRunningTaskScheduler.Start(task);
							runningTasks[storage+":"+file+":mp3"] = task;
							status["mp3"] = "building";
						}
					}
					if(File.Exists(basePath+"/"+storage+"/ogg/"+file))
						status["ogg"] = "ready";
					else {
						if(runningTasks.ContainsKey(storage+":"+file+":ogg"))
							status["ogg"] = "building";
						else {
							LongTask task = new LongTask(delegate {
								BuildOgg(storage, file);
							}, null, "Build OGG "+storage+":"+file, LongTaskPriority.Low);
							longRunningTaskScheduler.Start(task);
							runningTasks[storage+":"+file+":ogg"] = task;
							status["ogg"] = "building";
						}
					}
				}

				context.Response.StatusCode = 200;
				context.Response.Content = new JsonContent(json);
			}
		}
	}
}
