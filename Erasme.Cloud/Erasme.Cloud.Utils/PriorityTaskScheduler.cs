//
// PriorityTaskScheduler.cs
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
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Erasme.Cloud.Utils
{
	public class PriorityTaskScheduler
	{
		Thread[] threads;
		readonly int maximumConcurrencyLevel = Math.Max(1, Environment.ProcessorCount);
		readonly object instanceLock = new object();
		LinkedList<LongTask> lowTasks = new LinkedList<LongTask>();
		LinkedList<LongTask> normalTasks = new LinkedList<LongTask>();
		LinkedList<LongTask> highTasks = new LinkedList<LongTask>();
		LinkedList<LongTask> runningTasks = new LinkedList<LongTask>();

		public PriorityTaskScheduler(int maximumConcurrencyLevel): this(ThreadPriority.BelowNormal, maximumConcurrencyLevel)
		{
		}

		public PriorityTaskScheduler(ThreadPriority priority, int maximumConcurrencyLevel)
		{
			this.maximumConcurrencyLevel = maximumConcurrencyLevel;
			threads = new Thread[maximumConcurrencyLevel];
			for(int i = 0; i < threads.Length; i++) {
				threads[i] = new Thread(ThreadStart);
				threads[i].Name = "PriorityTaskScheduler Thread "+i;
				threads[i].Priority = priority;
				threads[i].IsBackground = true;
				threads[i].Start();
			}
		}

		void ThreadStart()
		{
			LongTask task = null;
			while(true) {
				lock(instanceLock) {
					if(task != null) {
						runningTasks.Remove(task);
						task = null;
					}
					if(highTasks.First != null) {
						task = highTasks.First.Value;
						highTasks.RemoveFirst();
					}
					else if(normalTasks.First != null) {
						task = normalTasks.First.Value;
						normalTasks.RemoveFirst();
					}
					else if(lowTasks.First != null) {
						task = lowTasks.First.Value;
						lowTasks.RemoveFirst();
					}
					else
						Monitor.Wait(instanceLock);
					if(task != null)
						runningTasks.AddLast(task);
				}
				if(task != null)
					task.Run();
			}
		}

		public int MaximumConcurrencyLevel
		{
			get {
				return maximumConcurrencyLevel;
			}
		}

		public LongTask[] Tasks
		{
			get {
				LongTask[] tasks;
				lock(instanceLock) {
					tasks = new LongTask[runningTasks.Count+highTasks.Count+
						normalTasks.Count+lowTasks.Count];
					runningTasks.CopyTo(tasks, 0);
					highTasks.CopyTo(tasks, runningTasks.Count);
					normalTasks.CopyTo(tasks, runningTasks.Count+highTasks.Count);
					lowTasks.CopyTo(tasks, runningTasks.Count+highTasks.Count+normalTasks.Count);
				}
				return tasks;
			}
		}

		public void Start(LongTask task)
		{
			lock(instanceLock) {
				if(task.Priority == LongTaskPriority.Low)
					lowTasks.AddLast(task);
				else if(task.Priority == LongTaskPriority.Normal)
					normalTasks.AddLast(task);
				else if(task.Priority == LongTaskPriority.High)
					highTasks.AddLast(task);
				Monitor.PulseAll(instanceLock);
			}
		}
	}
}

