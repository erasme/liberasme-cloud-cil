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
	
		void EnsureCanUpdateStorage(HttpContext context, string storage);

		void EnsureCanReadStorage(HttpContext context, string storage);

		void EnsureCanDeleteStorage(HttpContext context, string storage);

		void EnsureCanCreateFile(HttpContext context, string storage);

		void EnsureCanReadFile(HttpContext context, string storage);

		void EnsureCanUpdateFile(HttpContext context, string storage);
		
		void EnsureCanDeleteFile(HttpContext context, string storage);

		void EnsureCanCreateComment(HttpContext context, string storage, long file, string user);

		void EnsureCanUpdateComment(HttpContext context, string storage, long file, long comment);

		void EnsureCanDeleteComment(HttpContext context, string storage, long file, long comment, string owner);
	}

	public class DummyStorageRights: IStorageRights
	{
		public void EnsureCanCreateStorage(HttpContext context)
		{
		}

		public void EnsureCanUpdateStorage(HttpContext context, string storage)
		{
		}

		public void EnsureCanReadStorage(HttpContext context, string storage)
		{
		}

		public void EnsureCanDeleteStorage(HttpContext context, string storage)
		{
		}

		public void EnsureCanReadFile(HttpContext context, string storage)
		{
		}

		public void EnsureCanCreateFile(HttpContext context, string storage)
		{
		}

		public void EnsureCanUpdateFile(HttpContext context, string storage)
		{
		}

		public void EnsureCanDeleteFile(HttpContext context, string storage)
		{
		}

		public void EnsureCanCreateComment(HttpContext context, string storage, long file, string user)
		{
		}

		public void EnsureCanUpdateComment(HttpContext context, string storage, long file, long comment)
		{
		}

		public void EnsureCanDeleteComment(HttpContext context, string storage, long file, long comment, string owner)
		{
		}
	}
}
