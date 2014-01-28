//
// IMessageRights.cs
//
// Author:
//       Daniel Lacroix <dlacroix@erasme.org>
//
// Copyright (c) 2014 Departement du Rhone
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

using System;
using Erasme.Http;
using Erasme.Json;

namespace Erasme.Cloud.Message
{
	public interface IMessageRights
	{	
		void EnsureCanMonitorUser(HttpContext context, string user);

		void EnsureCanCreateMessage(HttpContext context, string origin, string destination);

		void EnsureCanReadMessage(HttpContext context, string origin, string destination);

		void EnsureCanUpdateMessage(HttpContext context, string origin, string destination);

		void EnsureCanDeleteMessage(HttpContext context, string origin, string destination);
	}

	public class DummyMessageRights: IMessageRights
	{
		public void EnsureCanMonitorUser(HttpContext context, string user)
		{
		}

		public void EnsureCanCreateMessage(HttpContext context, string origin, string destination)
		{
		}

		public void EnsureCanReadMessage(HttpContext context, string origin, string destination)
		{
		}

		public void EnsureCanUpdateMessage(HttpContext context, string origin, string destination)
		{
		}

		public void EnsureCanDeleteMessage(HttpContext context, string origin, string destination)
		{
		}
	}
}
