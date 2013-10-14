// SendmailService.cs
// 
//  Service to send simple text emails
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
using System.Net.Mail;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Erasme.Http;
using Erasme.Json;

namespace Erasme.Cloud.Mail
{
	public class SendmailService: IHttpHandler
	{				
		public string SmtpServer { get; private set; }
		
		public SendmailService(string host)
		{
			SmtpServer = host;
		}
		
		public Task ProcessRequestAsync(HttpContext context)
		{
			// POST / send a new message
			// content: { from: [from], to: [to], subject: [subject], body: [content] }
			if((context.Request.Method == "POST") && (context.Request.Path == "/")) {
				JsonValue json = context.Request.ReadAsJson();
				if(!json.ContainsKey("from") || !json.ContainsKey("to") ||
					!json.ContainsKey("subject") || !json.ContainsKey("body")) {
					context.Response.StatusCode = 400;
					context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
					context.Response.Content = new StringContent("Fields from,to,subject and body are needed");
				}
				else if(!(json["from"].Value is String) || !(json["to"].Value is String) ||
					!(json["subject"].Value is String) || !(json["body"].Value is String)) {
					context.Response.StatusCode = 403;
					context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
					context.Response.Content = new StringContent("Invalid fields format");
				}
				else {
					string fromString = (string)json["from"].Value;
					string toString = (string)json["to"].Value;
					string subjectString = (string)json["subject"].Value;
					string bodyString = (string)json["body"].Value;
			
					using(SmtpClient smtpClient = new SmtpClient(SmtpServer)) {
						smtpClient.Send(fromString, toString, subjectString, bodyString);
					}
					context.Response.StatusCode = 200;
					context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				}
			}
			// GET /?from=[from email]&to=[to email]&subject=[subject]&body=[email content]
			else if((context.Request.Method == "GET") && (context.Request.Path == "/")) {
				// check needed fields
				if(!context.Request.QueryString.ContainsKey("from") || !context.Request.QueryString.ContainsKey("to") ||
				   !context.Request.QueryString.ContainsKey("subject") || !context.Request.QueryString.ContainsKey("body")) {
					context.Response.StatusCode = 400;
					context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
					context.Response.Content = new StringContent("Fields from,to,subject and body are needed");
				}
				else {
					string fromString = context.Request.QueryString["from"];
					string toString = context.Request.QueryString["to"];
					string subjectString = context.Request.QueryString["subject"];
					string bodyString = context.Request.QueryString["body"];
			
					using(SmtpClient smtpClient = new SmtpClient(SmtpServer)) {
						smtpClient.Send(fromString, toString, subjectString, bodyString);
					}
					context.Response.StatusCode = 200;
					context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				}
			}
			return Task.FromResult<Object>(null);
		}
	}
}
