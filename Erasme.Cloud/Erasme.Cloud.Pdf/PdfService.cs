// PdfService.cs
// 
//  Get PDF files has images
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
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using Mono.Data.Sqlite;
using Erasme.Http;
using Erasme.Json;
using Erasme.Cloud;
using Erasme.Cloud.Storage;

namespace Erasme.Cloud.Pdf
{
	public class PdfService: HttpHandler
	{
		class UnoConv
		{
			Thread thread;
			internal string baseDirectory;

			public UnoConv(string baseDirectory)
			{
				this.baseDirectory = baseDirectory;
				thread = new Thread(ThreadStart);
				thread.Start();
			}

			void ThreadStart()
			{
				// unoconv listener
				ProcessStartInfo startInfo = new ProcessStartInfo("/usr/bin/unoconv", "--listener");
				startInfo.UseShellExecute = false;
				startInfo.WorkingDirectory = baseDirectory;
				startInfo.EnvironmentVariables["HOME"] = baseDirectory;

				using(Process process = new Process()) {
					process.StartInfo = startInfo;
					process.Start();
					process.WaitForExit();
				}
			}
		}

		static object globalLock = new object();
		static UnoConv unoconv = null;

		public StorageService Storage { get; private set; }
		string basePath;
		string temporaryDirectory;
		int cacheDuration;
		TaskFactory longRunningTaskFactory;

		object instanceLock = new object();
		Dictionary<string,Task> runningTasks = new Dictionary<string, Task>();
				
		public PdfService(string basepath, StorageService storage, string temporaryDirectory, int cacheDuration, TaskFactory longRunningTaskFactory)
		{
			basePath = basepath;
			this.temporaryDirectory = temporaryDirectory;
			this.cacheDuration = cacheDuration;
			this.longRunningTaskFactory = longRunningTaskFactory;

			if(!Directory.Exists(basepath))
				Directory.CreateDirectory(basepath);

			lock(globalLock) {
				if(unoconv == null) {
					if(!Directory.Exists(basepath+"/unoconv"))
						Directory.CreateDirectory(basepath+"/unoconv");
					unoconv = new UnoConv(basepath+"/unoconv");
				}
			}
			Storage = storage;
			Storage.FileCreated += OnFileCreated;
			Storage.FileChanged += OnFileChanged;
			Storage.FileDeleted += OnFileDeleted;
			Storage.StorageDeleted += OnStorageDeleted;
		}

		public static bool IsPdfCompatible(string mimetype)
		{
			return ((mimetype == "application/pdf") ||
			        // OpenDocument
			        (mimetype == "application/vnd.oasis.opendocument.text") ||
			        (mimetype == "application/vnd.oasis.opendocument.presentation") ||
					(mimetype == "application/vnd.oasis.opendocument.graphics") ||
			        (mimetype == "application/vnd.sun.xml.writer") ||
					// Microsoft PowerPoint
					(mimetype == "application/vnd.ms-powerpoint") ||
					// Microsoft Word
					(mimetype == "application/msword") ||
					// Microsoft Word 2007
			        (mimetype == "application/vnd.openxmlformats-officedocument.wordprocessingml.document") ||
			        // RichText
			        (mimetype == "text/richtext"));
		}

		void OnFileCreated(long storage, long file) 
		{
			string mimetype;
			string filename;
			long rev;
			Storage.GetDownloadFileInfo(storage, file, out mimetype, out filename, out rev);
			if(IsPdfCompatible(mimetype))
				QueuePdfGenerate(storage, file);
		}
		
		void OnFileChanged(long storage, long file)
		{
			// TODO: rebuild the converted files
		}
		
		void OnFileDeleted(long storage, long file)
		{
			if(Directory.Exists(basePath+"/"+storage+"/"+file))
				Directory.Delete(basePath+"/"+storage+"/"+file, true);
		}
		
		void OnStorageDeleted(long storage)
		{
			if(Directory.Exists(basePath+"/"+storage))
				Directory.Delete(basePath+"/"+storage, true);
		}

		public void QueuePdfGenerate(long storage, long file)
		{
			lock(instanceLock) {
				if(!runningTasks.ContainsKey(storage+":"+file)) {
					Task task = longRunningTaskFactory.StartNew(delegate {
						try {
							PdfGenerate(storage, file);
						} finally {
							// remove the task
							lock(instanceLock) {
								if(runningTasks.ContainsKey(storage+":"+file))
									runningTasks.Remove(storage+":"+file);
							}
						}
					});
					runningTasks[storage+":"+file] = task;
				}
			}
		}

		void PdfGenerate(long storage, long file)
		{
			BuildPdf(storage, file);
		}

		public JsonValue GetPdfInfo(long storage, long file)
		{
			JsonValue res = new JsonObject();
			string fileMimetype;
			string fileName;
			long fileRev;
			Storage.GetDownloadFileInfo(storage, file, out fileMimetype, out fileName, out fileRev);

			if(File.Exists(basePath+"/"+storage+"/"+file+"/info")) {
				res = JsonValue.Parse(File.ReadAllText(basePath+"/"+storage+"/"+file+"/info"));
				res["status"] = "ready";
			}
			else {
				res["storage"] = storage;
				res["file"] = file;
				if(IsPdfCompatible(fileMimetype)) {
					QueuePdfGenerate(storage, file);
					res["status"] = "building";
				}
				else {
					res["status"] = "invalid";
				}
			}
			return res;
		}

		struct Page
		{
			public double Width;
			public double Height;
		}

		public static bool ConvertToPdf(string filePath, string mimetype, string destPath)
		{
			string baseDirectory;
			lock(globalLock) {
				if(unoconv == null)
					throw new Exception("A PdfService instance need to be started to use ConvertToPdf");
				baseDirectory = unoconv.baseDirectory;
			}

			// unoconv
			string args = BuildArguments(new string[] {
				"-n", "--output", destPath, "-f", "pdf", filePath
			});
			ProcessStartInfo startInfo = new ProcessStartInfo("/usr/bin/unoconv", args);
			startInfo.UseShellExecute = false;
			startInfo.WorkingDirectory = baseDirectory;
			startInfo.EnvironmentVariables["HOME"] = baseDirectory;
			// abiword
//			string args = BuildArguments (new string[] {
//				"--to=PDF",	"-o", tmpFile, filePath
//			});
//			ProcessStartInfo startInfo = new ProcessStartInfo ("/usr/bin/abiword", args);
		
			using(Process process = new Process()) {
				process.StartInfo = startInfo;
				process.Start();
				process.WaitForExit();
			}

			return File.Exists(destPath);
		}

		bool BuildPdf(long storage, long file)
		{
			// create needed directories
			if(!Directory.Exists(basePath+"/"+storage))
				Directory.CreateDirectory(basePath+"/"+storage);
			if(!Directory.Exists(basePath+"/"+storage+"/"+file))
				Directory.CreateDirectory(basePath+"/"+storage+"/"+file);
			if(!Directory.Exists(basePath+"/"+storage+"/"+file+"/pages"))
				Directory.CreateDirectory(basePath+"/"+storage+"/"+file+"/pages");

			string fileMimetype;
			string fileName;
			long fileRev;
			Storage.GetDownloadFileInfo(storage, file, out fileMimetype, out fileName, out fileRev);

			string filePath = Storage.GetFullPath(storage, file);
			if(filePath == null)
				return false;

			if(fileMimetype != "application/pdf") {
				if(!ConvertToPdf(filePath, fileMimetype, basePath+"/"+storage+"/"+file+"/pdf"))
					return false;
				filePath = basePath+"/"+storage+"/"+file+"/pdf";
			}

			int countPages = 0;
			if(!BuildPages(filePath, basePath+"/"+storage+"/"+file+"/pages", out countPages))
				return false;

			// get the infos
			Dictionary<string,string> metas;
			List<Page> pages;
			BuildPdfInfo(filePath, out metas, out pages);

			// generate info
			JsonValue json = new JsonObject();
			json["storage"] = storage;
			json["file"] = file;
			json["rev"] = fileRev;
			json["fails"] = 0;
			JsonValue metasJson = new JsonObject();
			json["metas"] = metasJson;
			foreach(string key in metas.Keys) {
				metasJson[key] = metas[key];
			}
			JsonArray pagesJson = new JsonArray();
			json["pages"] = pagesJson;
			// create the pages
			for(int i = 0; i < countPages; i++) {
				Page page = new Page();
				if(i < pages.Count)
					page = pages[i];
				JsonValue pageJson = new JsonObject();
				pageJson["position"] = i;
				pageJson["width"] = Math.Round(page.Width);
				pageJson["height"] = Math.Round(page.Height);
				pagesJson.Add(pageJson);
			}
			string info = json.ToString();
			using(FileStream stream = File.OpenWrite(basePath+"/"+storage+"/"+file+"/info")) {
				byte[] bytes = Encoding.UTF8.GetBytes(info);
				stream.Write(bytes, 0, bytes.Length);
				stream.Close();
			}
			return true;
		}

		bool BuildPages(string pdfFile, string pagesPath, out int count)
		{
			string tmpDir = temporaryDirectory+"/"+Guid.NewGuid().ToString();
			Directory.CreateDirectory(tmpDir);

			// build the image of the page
			ProcessStartInfo startInfo = new ProcessStartInfo("/usr/bin/pdftoppm", BuildArguments(new string[] {
				"-jpeg", "-scale-to", "2048",
				pdfFile,
				tmpDir+"/page"
			}));

			using(Process process = new Process()) {
				process.StartInfo = startInfo;
				process.Start();
				process.WaitForExit();
			}

			int countPages = 0;
			foreach(string file in Directory.EnumerateFiles(tmpDir)) {
				int pos = file.IndexOf("page-");
				if(pos != -1) {
					string tmp = file.Substring(pos+5);
					long pagePosition = Convert.ToInt64(tmp.Substring(0, tmp.Length - 4));
					string destFile = pagesPath+"/"+(pagePosition-1);
					File.Move(file, destFile);
					countPages++;
				}
			}
			Directory.Delete(tmpDir, true);
			count = countPages;
			return true;
		}

		void BuildPdfInfo(string pdfFile, out Dictionary<string,string> metas, out List<Page> pages)
		{
			metas = new Dictionary<string, string>();
			pages = new List<Page>();

			// get the PDF infos
			ProcessStartInfo startInfo = new ProcessStartInfo("/usr/bin/pdfinfo", BuildArguments(new string[]{
				pdfFile,
				"-f", "1",
				"-l", "1000"
			}));
			startInfo.UseShellExecute = false;
			startInfo.RedirectStandardInput = true;
			startInfo.RedirectStandardOutput = true;
			startInfo.CreateNoWindow = true;

			string[] lines;
			using(Process process = new Process()) {
				process.StartInfo = startInfo;
				process.Start();
				process.WaitForExit();
				using(StreamReader output = process.StandardOutput) {
					lines = output.ReadToEnd().Split('\n');
				}
			}

			foreach(string line in lines) {
				int pos = line.IndexOf(':');
				if(pos != -1) {
					string key = line.Substring(0, pos);
					string value = line.Substring(pos+1);
					while(value.IndexOf(' ') == 0)
						value = value.Remove(0, 1);

					if(key == "Creator")
						metas["creator"] = value;
					else if(key == "Producer")
						metas["producer"] = value;
					else if(key == "CreationDate")
						metas["creationDate"] = value;
					else if(key == "Tagged")
						metas["tagged"] = value;
					else if(key == "Form")
						metas["form"] = value;
					else if(key == "Encrypted")
						metas["encrypted"] = value;
					else if(key == "Encrypted")
						metas["encrypted"] = value;
					else if(key == "Optimized")
						metas["optimized"] = value;
					else if(key == "PDF version")
						metas["pdfVersion"] = value;
					else if(key.StartsWith("Page") && key.EndsWith("size")) {
						string widthString = value.Substring(0, value.IndexOf('x'));
						string heightString = value.Substring(value.IndexOf('x')+2);
						heightString = heightString.Substring(0, heightString.IndexOf(' '));
						double width = Convert.ToDouble(widthString, CultureInfo.InvariantCulture);
						double height = Convert.ToDouble(heightString, CultureInfo.InvariantCulture);
						Page page = new Page();
						page.Width = width;
						page.Height = height;
						pages.Add(page);
					}
					else if(key.StartsWith("Page") && key.EndsWith("rot")) {
						int rot = Convert.ToInt32(value);
						if((rot == 90) || (rot == 270)) {
							Page page = pages[pages.Count-1];
							double tmp = page.Width;
							page.Width = page.Height;
							page.Height = tmp;
							pages[pages.Count-1] = page;
						}
					}
				}
			}
		}
				
		static string Enquote(string str)
		{
			return "'"+str.Replace("'", "\\'")+"'";
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

						
		public override void ProcessRequest(HttpContext context)
		{
			string[] parts = context.Request.Path.Split(new char[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
			long id = 0;
			long id2 = 0;
			long id3 = 0;

			// GET /[storage]/[file] get PDF info 
			if((context.Request.Method == "GET") && (parts.Length == 2) && long.TryParse(parts[0], out id) && long.TryParse(parts[1], out id2)) {
				context.Response.StatusCode = 200;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				context.Response.Content = new JsonContent(GetPdfInfo(id, id2));
			}
			// /[storage]/[file]/content[?attachement=1] get PDF file 
			else if((context.Request.Method == "GET") && (parts.Length == 3) && long.TryParse(parts[0], out id) && long.TryParse(parts[1], out id2) && (parts[2] == "content")) {
/*				context.Response.Content = new FileContent(basePath+"/"+id+"/"+id2+"/pages/"+id3);

				long parent;
				string mimetype;
				string name;
				long ctime, mtime, rev, size, position;
				Dictionary<string, string> meta;
				Storage.GetFileInfo(id, id2, out parent, out mimetype, out name, out ctime, out mtime, out rev, out size, out position, out meta);
				string filePath = null;
				if(mimetype == "application/pdf")
					filePath = Storage.GetFullPath(id, id2);
				else if(File.Exists(basePath+"/"+id+"/"+id2+"/pdf"))
					filePath = basePath+"/"+id+"/"+id2+"/pdf";
				if(filePath == null) {
					response.Status = 404;
					response.StringContent = "File not found";
				}
				else {
					string etag = "\""+id+":"+id2+":"+rev+"\"";
					if(!request.Header.Get.ContainsKey("nocache"))
						response.Http["ETag"] = etag;

					long argRev = -1;
					if(request.Header.Get.ContainsKey("rev"))
						argRev = Convert.ToInt64(request.Header.Get["rev"]);
					
					if(!request.Header.Get.ContainsKey("nocache") &&
					   request.Header.Http.ContainsKey("if-none-match") &&
					   (request.Header.Http["if-none-match"] == etag)) {
						response.Status = 304;
						if(argRev == rev)
							response.Http["Cache-Control"] = "max-age="+Server.Setup.DefaultCacheDuration;
					}
					else {
						response.Status = 200;
						if(argRev == rev)
							response.Http["Cache-Control"] = "max-age="+Server.Setup.DefaultCacheDuration;
						response.Http["Content-Type"] = "application/pdf";
						response.FileContent = filePath;
					}
				}
				return;*/
			}
			// get a page image /[storage]/[file]/pages/[page]/image
			else if((context.Request.Method == "GET") && (parts.Length == 5) && long.TryParse(parts[0], out id) && long.TryParse(parts[1], out id2) && (parts[2] == "pages") && long.TryParse(parts[3], out id3) && (parts[4] == "image")) {
				long rev = 0;
				string etag = "\""+id+":"+id2+":"+rev+"\"";
				if(!context.Request.QueryString.ContainsKey("nocache"))
					context.Response.Headers["etag"] = etag;

				long argRev = -1;
				if(context.Request.QueryString.ContainsKey("rev"))
					argRev = Convert.ToInt64(context.Request.QueryString["rev"]);
					
				if(!context.Request.QueryString.ContainsKey("nocache") &&
				   context.Request.Headers.ContainsKey("if-none-match") &&
				   (context.Request.Headers["if-none-match"] == etag)) {
					context.Response.StatusCode = 304;
					if(argRev == rev)
						context.Response.Headers["cache-control"] = "max-age="+cacheDuration;
				}
				else {
					context.Response.StatusCode = 200;
					if(argRev == rev)
						context.Response.Headers["cache-control"] = "max-age="+cacheDuration;
					context.Response.Headers["content-type"] = "image/jpeg";
					context.Response.Content = new FileContent(basePath+"/"+id+"/"+id2+"/pages/"+id3);
				}
			}
		}
	}
}
