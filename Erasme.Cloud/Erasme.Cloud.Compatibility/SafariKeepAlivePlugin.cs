// SafariKeepAlivePlugin.cs
// 
//  Disable any HTTP KeepAlive for Safari due to bugs
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
using System.Text.RegularExpressions;
using Erasme.Http;

namespace Erasme.Cloud.Compatibility
{
	public class SafariKeepAlivePlugin: HttpHandler
	{
		public SafariKeepAlivePlugin()
		{
		}
		
		public override void ProcessRequest(HttpContext context)
		{
			if(context.Request.Headers.ContainsKey("user-agent")) {
				string userAgent = context.Request.Headers["user-agent"];
				Regex r = new Regex(@"(iPad|iPhone).*Mobile\/.*Safari\/", RegexOptions.IgnoreCase);
				Regex r2 = new Regex(@"AppleCoreMedia\/", RegexOptions.IgnoreCase);
				if(r.Match(userAgent).Success || r2.Match(userAgent).Success) {
					// disable keep-alive
					context.Response.Headers["connection"] = "close";
				}
			}
		}
	}
}
