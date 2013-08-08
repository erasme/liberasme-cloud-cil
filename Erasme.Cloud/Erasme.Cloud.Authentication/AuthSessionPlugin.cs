// AuthSessionPlugin.cs
// 
//  The AuthSessionPlugin retrieve the auth session from the HttpContext
//
// Author(s):
//  Daniel Lacroix <dlacroix@erasme.org>
// 
// Copyright (c) 2011-2013 Departement du Rhone
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
using System.Collections.Generic;
using Erasme.Http;
using Erasme.Json;

namespace Erasme.Cloud.Authentication
{
	public class AuthSessionPlugin: HttpHandler
	{
		string headerKey;
		string cookieKey;
		AuthSessionService authSessionService;
		
		public AuthSessionPlugin(AuthSessionService authSessionService, string headerKey, string cookieKey)
		{
			this.authSessionService = authSessionService;
			this.headerKey = headerKey.ToLower();
			this.cookieKey = cookieKey;
		}
		
		public override void ProcessRequest(HttpContext context)
		{
			string key = null;
			if(context.Request.Headers.ContainsKey(headerKey))
				key = context.Request.Headers[headerKey];
			else if(context.Request.Cookies.ContainsKey(cookieKey))
				key = context.Request.Cookies[cookieKey];
			if(key != null) {
				JsonValue authSession = authSessionService.Get(key);
				if(authSession != null)
					context.User = authSession["user"];
			}
		}
	}
}
