// MimeIconService.cs
// 
//  Provide an icon for each given mimetype
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
using System.Net;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using Erasme.Http;
using Erasme.Cloud;

namespace Erasme.Cloud.Mime
{
	public class MimeIconService: HttpHandler
	{
		string basedir;
		int cacheDuration;
		
		public MimeIconService(string basedir, int cacheDuration)
		{
			if(Path.IsPathRooted(basedir))
				this.basedir = Path.GetFullPath(basedir);
			else
				this.basedir = Path.GetFullPath(Environment.CurrentDirectory+"/"+basedir);
			this.cacheDuration = cacheDuration;
		}
		
		public override void ProcessRequest(HttpContext context)
		{
			if((context.Request.Method == "GET")) {
				string mimetype;
				if(context.Request.QueryString.ContainsKey("mimetype"))
					mimetype = context.Request.QueryString["mimetype"];
				else {
					if(context.Request.Path.StartsWith("/"))
						mimetype = context.Request.Path.Substring(1);
					else
						mimetype = String.Empty;
				}

				Console.WriteLine("MimeIconService mimetype: "+mimetype);

				string file = basedir+"/"+HttpUtility.UrlEncode(mimetype)+".png";

				Console.WriteLine("MimeIconService test file: "+file);

				if(!File.Exists(file)) {
					string[] splitted = mimetype.Split('/');
					if(File.Exists(basedir+"/"+HttpUtility.UrlEncode(splitted[0])+".png"))
						file = basedir+"/"+HttpUtility.UrlEncode(splitted[0])+".png";
					else
						file = basedir+"/default.png";
				}
				context.Response.StatusCode = 200;
				context.Response.Headers["content-type"] = "image/png";
				context.Response.Headers["cache-control"] = "max-age="+cacheDuration;
				context.Response.Content = new FileContent(file);
			}
		}
	}
}

