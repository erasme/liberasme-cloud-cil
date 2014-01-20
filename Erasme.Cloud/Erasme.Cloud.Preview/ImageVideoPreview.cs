// ImageVideoPreview.cs
// 
//  Get an image preview of an image or a video
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
using System.Globalization;
using System.Collections.Generic;

namespace Erasme.Cloud.Preview
{	
	public class ImageVideoPreview: IPreview
	{
		string temporaryDirectory;

		public ImageVideoPreview(string temporaryDirectory)
		{
			this.temporaryDirectory = temporaryDirectory;
		}
		
		public string Process(string file, string mimetype, int width, int height, out PreviewFormat format, out string error)
		{
			error = null;
			bool deleteFile = false;
			string tmpFile = temporaryDirectory+"/"+Guid.NewGuid().ToString();
			
			ProcessStartInfo startInfo;
			if(mimetype.StartsWith("image/") || mimetype.StartsWith("audio/")) {
				if(mimetype.StartsWith("audio/")) {
					if(!ExtractCover(file, tmpFile)) {
						format = PreviewFormat.JPEG;
						return null;
					}
					deleteFile = true;
					file = tmpFile;
					tmpFile = temporaryDirectory+"/"+Guid.NewGuid().ToString();
				}

				double naturalWidth;
				double naturalHeight;
				GetImageSize(file, out naturalWidth, out naturalHeight);

				bool resizeNeeded = (naturalWidth == 0) || (naturalHeight == 0) ||
					(naturalWidth > width) || (naturalHeight > height);

				List<string> argsList = new List<string>();

				bool needTransparency =
					(mimetype == "image/png") || (mimetype == "image/x-tga") || (mimetype == "image/svg+xml");

				if(mimetype == "image/png")
					argsList.Add("png:"+file+"[0]");
				else if(mimetype == "image/x-tga")
					argsList.Add("tga:"+file+"[0]");
				else
					argsList.Add("file://"+file+"[0]");

				argsList.Add("-auto-orient");
				argsList.Add("-strip");
				if(resizeNeeded) {
					argsList.Add("-resize");
					argsList.Add(width+"x"+height);
				}

				if(needTransparency) {
					argsList.Add("png:"+tmpFile);
					format = PreviewFormat.PNG;
				}
				else {
					argsList.Add("-quality");
					argsList.Add("80");
					argsList.Add("jpeg:"+tmpFile);
					format = PreviewFormat.JPEG;
				}
				startInfo = new ProcessStartInfo("/usr/bin/convert", BuildArguments(argsList));
			}
			// video
			else {
				// get media info
				double duration = GetVideoDuration(file);
				double offset = 20;
				if(duration < 5)
					offset = 0;
				else if(duration < 10)
					offset = 4;
				else if(duration < 20)
					offset = 8;
				else if(duration < 30)
					offset = 19;

				double videoWidth, videoHeight;
				GetVideoSize(file, out videoWidth, out videoHeight);

				double rotation = GetVideoRotation(file);

				List<string> argsList = new List<string>();
				argsList.Add("-ss");
				argsList.Add(((int)offset).ToString());
				argsList.Add("-i");
				argsList.Add(file);
				if(rotation == 90) {
					double tmp = videoHeight;
					videoHeight = videoWidth;
					videoWidth = tmp;
					argsList.Add("-vf");
					argsList.Add("transpose=0,hflip");
				}
				else if(rotation == 180) {
					argsList.Add("-vf");
					argsList.Add("vflip,hflip");
				}
				else if(rotation == 270) {
					double tmp = videoHeight;
					videoHeight = videoWidth;
					videoWidth = tmp;
					argsList.Add("-vf");
					argsList.Add("transpose=0,vflip");
				}
				double scaleWidth = width;
				double scaleHeight = height;

				double videoRatio = videoWidth/videoHeight;
				double destRatio = width/height;

				if(videoRatio > destRatio)
					scaleHeight = Math.Round(scaleWidth / videoRatio);
				else
					scaleWidth = Math.Round(scaleHeight * videoRatio);

				argsList.Add("-s");
				argsList.Add(scaleWidth+"x"+scaleHeight);

				argsList.Add("-vframes");
				argsList.Add("1");
				argsList.Add("-f");
				argsList.Add("image2");
				argsList.Add(tmpFile);

				startInfo = new ProcessStartInfo("/usr/bin/ffmpegstatic", BuildArguments(argsList));
				format = PreviewFormat.JPEG;
			}

			startInfo.RedirectStandardError = true;
			startInfo.RedirectStandardOutput = true;
			startInfo.UseShellExecute = false;

			using(Process process = new Process()) {
				process.StartInfo = startInfo;
				process.Start();
				process.WaitForExit();

				if(deleteFile)
					File.Delete(file);

				if(process.ExitCode == 0)
					return tmpFile;
				else {
					error = 
						"cmdline: "+startInfo.FileName+" "+startInfo.Arguments+"\n"+
						"stout:"+process.StandardOutput.ReadToEnd()+"\n"+
						"stderr:"+process.StandardError.ReadToEnd()+"\n";
					Console.WriteLine(error);
					if(File.Exists(tmpFile))
						File.Delete(tmpFile);
					return null;
				}
			}
		}

		/// <summary>
		/// Gets the size of the image.
		/// </summary>
		/// <param name='file'>
		/// File path.
		/// </param>
		/// <param name='width'>
		/// Width.
		/// </param>
		/// <param name='height'>
		/// Height.
		/// </param>
		public static void GetImageSize(string file, out double width, out double height)
		{
			width = 0;
			height = 0;
			// get media info
			string args = BuildArguments(new string[]{ "--Inform=Image;%Width%:%Height%", file });
			ProcessStartInfo startInfo = new ProcessStartInfo("/usr/bin/mediainfo", args);
			startInfo.RedirectStandardError = true;
			startInfo.RedirectStandardOutput = true;
			startInfo.UseShellExecute = false;

			using(Process process = new Process()) {
				process.StartInfo = startInfo;
				process.Start();
				process.WaitForExit();
				if(process.ExitCode == 0) {
					string lines = process.StandardOutput.ReadToEnd().TrimEnd(' ', '\n');
					string[] tab = lines.Split(':');
					if(tab.Length == 2) {
						double.TryParse(tab[0], NumberStyles.Any, CultureInfo.InvariantCulture, out width);
						double.TryParse(tab[1], NumberStyles.Any, CultureInfo.InvariantCulture, out height);
					}
				}
			}
		}

		/// <summary>
		/// Gets the video rotation in degree.
		/// </summary>
		/// <returns>
		/// The video rotation.
		/// </returns>
		/// <param name='file'>
		/// File path.
		/// </param>
		public static double GetVideoRotation(string file)
		{
			double rotation = 0;
			// get media info
			string args = BuildArguments(new string[]{ "--Inform=Video;%Rotation%", file });
			ProcessStartInfo startInfo = new ProcessStartInfo("/usr/bin/mediainfo", args);
			startInfo.RedirectStandardError = true;
			startInfo.RedirectStandardOutput = true;
			startInfo.UseShellExecute = false;

			using(Process process = new Process()) {
				process.StartInfo = startInfo;
				process.Start();
				process.WaitForExit();
				if(process.ExitCode == 0) {
					string lines = process.StandardOutput.ReadToEnd().TrimEnd(' ', '\n');
					double.TryParse(lines, NumberStyles.Any, CultureInfo.InvariantCulture, out rotation);
				}
			}
			return rotation;
		}

		/// <summary>
		/// Gets the size of the video.
		/// </summary>
		/// <param name='file'>
		/// File path.
		/// </param>
		/// <param name='width'>
		/// Width.
		/// </param>
		/// <param name='height'>
		/// Height.
		/// </param>
		public static void GetVideoSize(string file, out double width, out double height)
		{
			width = 0;
			height = 0;
			// get media info
			string args = BuildArguments(new string[]{ "--Inform=Video;%Width%:%Height%", file });
			ProcessStartInfo startInfo = new ProcessStartInfo("/usr/bin/mediainfo", args);
			startInfo.RedirectStandardError = true;
			startInfo.RedirectStandardOutput = true;
			startInfo.UseShellExecute = false;

			using(Process process = new Process()) {
				process.StartInfo = startInfo;
				process.Start();
				process.WaitForExit();
				if(process.ExitCode == 0) {
					string lines = process.StandardOutput.ReadToEnd().TrimEnd(' ', '\n');
					string[] tab = lines.Split(':');
					if(tab.Length == 2) {
						double.TryParse(tab[0], NumberStyles.Any, CultureInfo.InvariantCulture, out width);
						double.TryParse(tab[1], NumberStyles.Any, CultureInfo.InvariantCulture, out height);
					}
				}
			}
		}

		/// <summary>
		/// Gets the duration of the video in seconds.
		/// </summary>
		/// <returns>
		/// The video duration.
		/// </returns>
		/// <param name='file'>
		/// Video file path.
		/// </param>
		public static double GetVideoDuration(string file)
		{
			double duration = 0;
			// get media info
			string args = BuildArguments(new string[]{ "-i", file });
			ProcessStartInfo startInfo = new ProcessStartInfo("/usr/bin/ffmpegstatic", args);
			startInfo.RedirectStandardError = true;
			startInfo.RedirectStandardOutput = true;
			startInfo.UseShellExecute = false;

			using(Process process = new Process()) {
				process.StartInfo = startInfo;
				process.Start();
				process.WaitForExit();
				string[] lines = process.StandardError.ReadToEnd().Split('\n');
				foreach(string line in lines) {
					if(line.IndexOf("Duration: ") != -1) {
						string durationString = line.Substring(line.IndexOf("Duration: ")+10);
						if(durationString.IndexOf(',') != -1)
							durationString = durationString.Substring(0, durationString.IndexOf(','));
						TimeSpan durationSpan;
						if(TimeSpan.TryParse(durationString, out durationSpan))
							duration = durationSpan.TotalSeconds;
					}
				}
			}
			return duration;
		}

		public static bool HasCover(string file)
		{
			bool hasCover = false;
			// get media info
			string args = BuildArguments(new string[]{ "--Inform=General;%Cover%", file });
			ProcessStartInfo startInfo = new ProcessStartInfo("/usr/bin/mediainfo", args);
			startInfo.RedirectStandardError = true;
			startInfo.RedirectStandardOutput = true;
			startInfo.UseShellExecute = false;

			using(Process process = new Process()) {
				process.StartInfo = startInfo;
				process.Start();
				process.WaitForExit();
				if(process.ExitCode == 0) {
					string lines = process.StandardOutput.ReadToEnd().TrimEnd(' ', '\n');
					hasCover = lines.ToLower().Contains("yes");
				}
			}
			return hasCover;
		}

		public static bool ExtractCover(string file, string toFile)
		{
			// get media info
			string args = BuildArguments(new string[]{ "-i", file, "-an", "-vcodec", "copy", "-f", "image2", toFile });
			ProcessStartInfo startInfo = new ProcessStartInfo("/usr/bin/ffmpegstatic", args);
			startInfo.RedirectStandardError = true;
			startInfo.RedirectStandardOutput = true;
			startInfo.UseShellExecute = false;

			using(Process process = new Process()) {
				process.StartInfo = startInfo;
				process.Start();
				process.WaitForExit();
				if(process.ExitCode != 0) {
					return false;
				}
			}
			return File.Exists(toFile);
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

		static string BuildArguments(List<string> args)
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
