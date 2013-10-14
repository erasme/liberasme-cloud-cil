// IStorageRights.cs
// 
//  Interface that define classes that can handle right management
//  for StorageService
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
using Erasme.Http;
using Erasme.Json;

namespace Erasme.Cloud.Storage
{
	public interface IStorageRights
	{	
		void EnsureCanCreateStorage(HttpContext context);
	
		void EnsureCanUpdateStorage(HttpContext context, long storage);

		void EnsureCanReadStorage(HttpContext context, long storage);

		void EnsureCanDeleteStorage(HttpContext context, long storage);

		void EnsureCanCreateFile(HttpContext context, long storage);

		void EnsureCanReadFile(HttpContext context, long storage);

		void EnsureCanUpdateFile(HttpContext context, long storage);
		
		void EnsureCanDeleteFile(HttpContext context, long storage);

		void EnsureCanCreateComment(HttpContext context, long storage, long file, long user);

		void EnsureCanUpdateComment(HttpContext context, long storage, long file, long comment);

		void EnsureCanDeleteComment(HttpContext context, long storage, long file, long comment);
	}

	public class DummyStorageRights: IStorageRights
	{
		public void EnsureCanCreateStorage(HttpContext context)
		{
		}

		public void EnsureCanUpdateStorage(HttpContext context, long storage)
		{
		}

		public void EnsureCanReadStorage(HttpContext context, long storage)
		{
		}

		public void EnsureCanDeleteStorage(HttpContext context, long storage)
		{
		}

		public void EnsureCanReadFile(HttpContext context, long storage)
		{
		}

		public void EnsureCanCreateFile(HttpContext context, long storage)
		{
		}

		public void EnsureCanUpdateFile(HttpContext context, long storage)
		{
		}

		public void EnsureCanDeleteFile(HttpContext context, long storage)
		{
		}

		public void EnsureCanCreateComment(HttpContext context, long storage, long file, long user)
		{
		}

		public void EnsureCanUpdateComment(HttpContext context, long storage, long file, long comment)
		{
		}

		public void EnsureCanDeleteComment(HttpContext context, long storage, long file, long comment)
		{
		}
	}
}
