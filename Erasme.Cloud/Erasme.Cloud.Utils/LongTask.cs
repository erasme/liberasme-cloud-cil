//
// LongTask.cs
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
using System.Threading.Tasks;

namespace Erasme.Cloud.Utils
{
	public enum LongTaskPriority
	{
		Low,
		Normal,
		High
	}

	public enum LongTaskStatus
	{
		Waiting,
		Running,
		Completed
	}

	public class LongTask
	{
		object globalLock = new object();
		static long idGen = 0;

		object instanceLock = new object();
		LongTaskStatus status = LongTaskStatus.Waiting;
		Exception exception = null;
		TaskCompletionSource<object> source = new TaskCompletionSource<object>(); 

		public LongTask(Action action, string owner, string description): this(action, owner, description, LongTaskPriority.Normal)
		{
		}

		public LongTask(Action action, string owner, string description, LongTaskPriority priority)
		{
			Action = action;
			Task = source.Task;
			CreateDate = DateTime.Now;
			Owner = owner;
			Description = description;
			Priority = priority;
			lock(globalLock) {
				Id = (++idGen).ToString();
			}
		}

		public string Id { get; private set; }

		public Task Task { get; private set; }

		public LongTaskStatus Status {
			get {
				LongTaskStatus status;
				lock(instanceLock) {
					status = this.status;
				}
				return status;
			}
			internal set {
				lock(instanceLock) {
					this.status = value;
				}
			}
		 }

		public Exception Exception {
			get {
				Exception exception;
				lock(instanceLock) {
					exception = this.exception;
				}
				return exception;
			}
			internal set {
				lock(instanceLock) {
					this.exception = value;
				}
			}
		}

		public Action Action { get; private set; }

		public LongTaskPriority Priority { get; private set; }

		public DateTime CreateDate { get; private set; }

		public string Description { get; private set; }

		public string Owner { get; private set; }

		internal void Run()
		{
			try {
				Status = LongTaskStatus.Running;
				Action.Invoke();
				source.TrySetResult(null);
			}
			catch(Exception e) {
				Exception = e;
				source.TrySetException(e);
			}
			finally {
				Status = LongTaskStatus.Completed;
			}
		}

		public void Abort()
		{
			// TODO
		}
	}
}
