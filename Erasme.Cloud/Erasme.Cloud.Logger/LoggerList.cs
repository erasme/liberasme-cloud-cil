// LoggerList.cs
// 
//  A logging service to log on several logging service
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
using System.Collections.Generic;

namespace Erasme.Cloud.Logger
{
	public class LoggerList: ILogger, IDisposable
	{
		object instanceLock = new object();
		List<ILogger> list = new List<ILogger>();
		
		public LoggerList()
		{
		}
		
		public void Add(ILogger logger)
		{
			lock(instanceLock) {
				list.Add(logger);
			}
		}
		
		public void Remove(ILogger logger)
		{
			lock(instanceLock) {
				list.Remove(logger);
			}
		}
		
		public void Log(LogLevel level, string message)
		{
			List<ILogger> tmp;
			lock(instanceLock) {
				tmp = new List<ILogger>(list);
			}
			foreach(ILogger logger in tmp)
				logger.Log(level, message);
		}
		
		public void Dispose()
		{
			foreach(ILogger logger in list) {
				IDisposable disposable = logger as IDisposable;
				if(disposable != null)
					disposable.Dispose();
			}
		}
	}
}

