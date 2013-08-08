// ProxyService.cs
// 
//  Relay an HTTP request service
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
using System.Collections.Generic;
using System.Threading.Tasks;
using Erasme.Http;
using Erasme.Cloud;

namespace Erasme.Cloud.HttpProxy
{
	public class ProxyService: IHttpHandler
	{		
		public ProxyService()
		{
		}

		public async Task ProcessRequestAsync(HttpContext context)
		{
			if((context.Request.Method == "GET") && context.Request.QueryString.ContainsKey("url")) {
				System.Net.WebRequest webRequest = System.Net.WebRequest.Create(context.Request.QueryString["url"]);
				string contentType = null;
				if(context.Request.QueryString.ContainsKey("contenttype"))
					contentType = context.Request.QueryString["contenttype"];
				webRequest.Method = "GET";
				HttpWebRequest httpWebRequest = webRequest as HttpWebRequest;
				if(httpWebRequest != null)
					httpWebRequest.AllowAutoRedirect = true;
				HttpWebResponse response = await webRequest.GetResponseAsync() as HttpWebResponse;

				if(contentType == null)
					contentType = response.ContentType;

				context.Response.StatusCode = (int)response.StatusCode;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				context.Response.Headers["content-type"] = contentType;
				context.Response.Content = new StreamContent(response.GetResponseStream());
			}
		}
	}
}
