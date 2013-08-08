// TestCloudServer.cs
// 
//  Override default HttpServer to display log in the console
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
using System.Globalization;
using System.Threading.Tasks;
using Erasme.Http;

namespace TestCloudServer
{
	public class TestCloudServer: HttpServer
	{
		public TestCloudServer(int port): base(port)
		{
		}

		protected override async Task ProcessRequestAsync(HttpContext context)
		{
			await base.ProcessRequestAsync(context);
			// log the request

			// log date
			string log = "["+String.Format("{0:yyyy/MM/dd HH:mm:ss}", DateTime.Now)+"] ";
			// remote address
			log += context.Request.RemoteEndPoint.ToString()+" ";
			// user
			if(context.User != null)
				log += context.User+" ";
			else
				log += "- ";
			// request 
			log += "\""+context.Request.Method+" "+context.Request.FullPath+"\" ";
			// response
			if(context.WebSocket != null)
				log += "WS ";
			else
				log += context.Response.StatusCode+" ";
			// bytes received
			log += context.Request.ReadCounter+"/"+context.Request.WriteCounter+" ";
			// time
			log += Math.Round((DateTime.Now - context.Request.StartTime).TotalMilliseconds).ToString(CultureInfo.InvariantCulture)+"ms";

			// write the log
			Console.WriteLine(log);
		}

		protected override void OnWebSocketHandlerMessage(WebSocketHandler handler, string message)
		{
			// log the message

			// log date
			string log = "["+String.Format("{0:yyyy/MM/dd HH:mm:ss}", DateTime.Now)+"] ";
			// remote address
			log += handler.Context.Request.RemoteEndPoint.ToString()+" ";
			// user
			if(handler.Context.User != null)
				log += handler.Context.User+" ";
			else
				log += "- ";
			// request 
			log += "\"WSMI "+handler.Context.Request.FullPath+"\" \""+message+"\"";

			// write the log
			Console.WriteLine(log);

			// handle the message
			base.OnWebSocketHandlerMessage(handler, message);
		}

		protected override void WebSocketHandlerSend(WebSocketHandler handler, string message)
		{
			base.WebSocketHandlerSend(handler, message);

			// log the message

			// log date
			string log = "["+String.Format("{0:yyyy/MM/dd HH:mm:ss}", DateTime.Now)+"] ";
			// remote address
			log += handler.Context.Request.RemoteEndPoint.ToString()+" ";
			// user
			if(handler.Context.User != null)
				log += handler.Context.User+" ";
			else
				log += "- ";
			// request 
			log += "\"WSMO "+handler.Context.Request.FullPath+"\" \""+message+"\"";

			// write the log
			Console.WriteLine(log);
		}

		protected override void OnProcessRequestError(HttpContext context, Exception exception)
		{
			base.OnProcessRequestError(context, exception);

			// log date
			string log = "["+String.Format("{0:yyyy/MM/dd HH:mm:ss}", DateTime.Now)+"] ";
			// remote address
			log += context.Request.RemoteEndPoint.ToString()+" ";
			// user
			if(context.User != null)
				log += context.User+" ";
			else
				log += "- ";

			// request 
			log += "\""+context.Request.Method+" "+context.Request.FullPath+"\" ";
			// response
			if(context.WebSocket != null)
				log += "WS ";
			else
				log += context.Response.StatusCode+" ";
			// bytes received
			log += context.Request.ReadCounter+"/"+context.Request.WriteCounter+" ";
			// time
			log += Math.Round((DateTime.Now - context.Request.StartTime).TotalMilliseconds).ToString(CultureInfo.InvariantCulture)+"ms\n";
			// exception details
			log += exception.ToString();

			// write the log
			Console.WriteLine(log);
		}
	}
}

