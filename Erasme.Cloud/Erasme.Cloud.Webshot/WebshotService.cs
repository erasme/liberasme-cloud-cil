// WebShotService.cs
// 
//  Provide a capture of a given web page and return an image
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
using System.Diagnostics;
using System.Collections.Generic;
using Erasme.Http;

namespace Erasme.Cloud.Webshot
{
	public class WebshotService: HttpHandler
	{
		string basePath;
		Erasme.Cloud.Cache.Cache cache;
		int width;
		int height;
		long timeout;
		string tmpDir;

		const string webshotScript = @"
var page = require('webpage').create(),
    system = require('system'),
    address, output, size, delay;

if(system.args.length < 3 || system.args.length > 5) {
	console.log('Usage: webshot.js URL filename [widthxheight] [delayms]');
	phantom.exit(1);
}
else {
	address = system.args[1];
	output = system.args[2];
	page.viewportSize = { width: 1024, height: 768 };
	if(system.args.length > 3) {
		size = system.args[3].split('x');
		page.zoomFactor = (new Number(size[0]))/1024;
		page.clipRect = { top: 0, left: 0, width: new Number(size[0]), height: new Number(size[1]) };
		page.viewportSize = { width: new Number(size[0]), height: new Number(size[1]) };
    }
	if(system.args.length > 4)
		delay = new Number(system.args[4]);
    page.open(address, function (status) {
        if (status !== 'success') {
			// Unable to load the address!
			phantom.exit(1);
        }
		else {
            window.setTimeout(function () {
                page.render(output);
                phantom.exit();
            }, delay);
        }
    });
}
";

		public WebshotService(string basepath, int width, int height, long timeout, string tmpDir)
		{
			basePath = basepath;
			this.width = width;
			this.height = height;
			this.timeout = timeout;
			cache = new Erasme.Cloud.Cache.Cache(basePath+"/cache/", timeout, OnCacheMissed);
			this.tmpDir = tmpDir;
		}

		public static string BuildWebshot(string tmpDir, string url, int width, int height)
		{
			string fileId = Guid.NewGuid().ToString();
			string filename = tmpDir+"/"+fileId+".jpg";

			string args = BuildArguments(new string[]{
				"--ignore-ssl-errors=yes",
				"--ssl-protocol=any",
				"/dev/stdin",
				url,
				filename,
				width+"x"+height,
				"200"
			});

			ProcessStartInfo startInfo = new ProcessStartInfo("/usr/bin/phantomjs", args);
			startInfo.RedirectStandardOutput = true;
			startInfo.RedirectStandardInput = true;
			startInfo.UseShellExecute = false;
			Process process = new Process();
			process.StartInfo = startInfo;
			process.Start();

			// write the JS script to stdin
			process.StandardInput.Write(webshotScript);
			process.StandardInput.Close();

			process.WaitForExit();
			int exitCode = process.ExitCode;
			process.Dispose();

			if(exitCode != 0)
				return null;
			return filename;
		}

		string OnCacheMissed(string key, out string validity)
		{
			string fileId = Guid.NewGuid().ToString();
			string filename = basePath+"/"+fileId+".png";
			validity = "";

			string tmpFile = BuildWebshot(tmpDir, key, width, height);
			if(tmpFile != null)
				File.Move(tmpFile, filename);
			return filename;
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
			if((context.Request.Method == "GET") && context.Request.QueryString.ContainsKey("url")) {
				context.Response.StatusCode = 200;
				context.Response.Headers["content-type"] = "image/jpeg";
				context.Response.Headers["cache-control"] = "max-age="+timeout;
				context.Response.Content = new FileContent(cache.GetItem(context.Request.QueryString["url"]));
			}
		}
	}
}
