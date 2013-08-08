// UrlPreview.cs
// 
//  Get an image preview of a URL
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
using Erasme.Cloud.Webshot;

namespace Erasme.Cloud.Preview
{	
	public class UrlPreview: IPreview
	{
		string temporaryDirectory;

		public UrlPreview(string temporaryDirectory)
		{
			this.temporaryDirectory = temporaryDirectory;
		}
		
		public string Process(string file, string mimetype, int width, int height, out PreviewFormat format, out string error)
		{
			error = null;
			format = PreviewFormat.JPEG;
			string url = File.ReadAllText(file).Trim(' ', '\t', '\n');
			return WebshotService.BuildWebshot(temporaryDirectory, url, width, height);
		}
	}
}
