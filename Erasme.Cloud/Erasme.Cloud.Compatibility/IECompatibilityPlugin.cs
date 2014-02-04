// IECompatibilityPlugin.cs
// 
//  Force IE7 rendering for IE8 to avoid IE8 bugs
//  Set text/plain for application/json for IE7 and IE8. Else
//  IE try to download the file.
//
//  This plugin need to be set after the services to allow content-type
//  changes.
//
// Author(s):
//  Daniel Lacroix <dlacroix@erasme.org>
// 
// Copyright (c) 2012-2014 Departement du Rhone
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
using System.Text.RegularExpressions;
using Erasme.Http;

namespace Erasme.Cloud.Compatibility
{
	public class IECompatibilityPlugin: HttpHandler
	{
		public IECompatibilityPlugin()
		{
		}
		
		public override void ProcessRequest(HttpContext context)
		{
			if(context.Request.Headers.ContainsKey("user-agent")) {
				string userAgent = context.Request.Headers["user-agent"];
				Regex r = new Regex(@" MSIE (7|8)\.0;", RegexOptions.IgnoreCase);
				if(r.Match(userAgent).Success) {
					context.Response.Headers["x-ua-compatible"] = "IE=EmulateIE7";
					// replace application/json by text/plain for IE <9 to avoid file download in the browser
					if(context.Response.Headers.ContainsKey("content-type")) {
						string contentType = context.Response.Headers["content-type"];
						if(contentType.Contains("application/json"))
							context.Response.Headers["content-type"] = contentType.Replace("application/json", "text/plain");
					}
					else if((context.Response.Content != null) && context.Response.Content.Headers.ContentType.Contains("application/json"))
						context.Response.Headers["content-type"] = context.Response.Content.Headers.ContentType.Replace("application/json", "text/plain");
				}
			}
		}
	}
}

