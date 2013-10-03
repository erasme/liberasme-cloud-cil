// PdfPreview.cs
// 
//  Get an image preview of a PDF file
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
using System.Threading;
using System.Diagnostics;

namespace Erasme.Cloud.Preview
{	
	public class PdfPreview: IPreview
	{
		string temporaryDirectory;

		public PdfPreview(string temporaryDirectory)
		{
			this.temporaryDirectory = temporaryDirectory;
		}
			
		public string Process(string file, string mimetype, int width, int height, out PreviewFormat format, out string error)
		{
			error = null;
			format = PreviewFormat.JPEG;
			if(!Pdf.PdfService.IsPdfCompatible(mimetype)) {
				error = "File format '"+mimetype+"' is not compatible with this service";
				return null;
			}

			string tmpFile = temporaryDirectory+"/"+Guid.NewGuid().ToString();
			string pdfFile = file;

			if(mimetype != "application/pdf") {
				pdfFile = tmpFile+".pdf";
				if(!Pdf.PdfService.ConvertToPdf(file, mimetype, pdfFile)) {
					error = "PDF conversion fails";
					return null;
				}
			}

			// build the image of the page
			ProcessStartInfo startInfo = new ProcessStartInfo("/usr/bin/pdftoppm", BuildArguments(new string[] {
				"-f", "1", "-l", "1", "-jpeg", "-scale-to", (Math.Min(width, height)).ToString(),
				pdfFile,
				tmpFile
			}));

			using(Process process = new Process()) {
				process.StartInfo = startInfo;
				process.Start();
				process.WaitForExit();
			}
			if(file != pdfFile)
				File.Delete(pdfFile);
			if(File.Exists(tmpFile + "-1.jpg"))
				return tmpFile + "-1.jpg";
			else if(File.Exists(tmpFile + "-01.jpg"))
				return tmpFile + "-01.jpg";
			else if(File.Exists(tmpFile + "-001.jpg"))
				return tmpFile + "-001.jpg";
			else
				return null;
		}
				
		static string BuildArguments(string[] args)
		{
			string res = "";
			foreach(string arg in args) {
				string tmp = (string)arg.Clone();
				tmp = tmp.Replace("'", "\\'");
				if(res != "")
					res += " ";
				res += "'"+tmp+"'";
			}
			return res;
		}		
	}
}
