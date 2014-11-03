// ResourcesLocker.cs
// 
//  Provide a way to lock access to a resource represented
//  by a string
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
using System.Threading;
using System.Collections.Generic;

namespace Erasme.Cloud.Utils
{
	public class ResourcesLocker
	{
		object instanceLock = new object();
		Dictionary<string,LockWrapper> resourceLocks = new Dictionary<string, LockWrapper>();

		class LockWrapper
		{
			public object instanceLock;
			public int counter;
			public bool taken;
		}

		public class Resource: IDisposable
		{
			string resource;
			ResourcesLocker locker;
		
			internal Resource(ResourcesLocker locker, string resource)
			{
				this.locker = locker;
				this.resource = resource;
			}
			
			public void Dispose()
			{
				bool last = true;
				LockWrapper wrapper;
				lock(locker.instanceLock) {
					wrapper = locker.resourceLocks[resource];
					if(wrapper.counter == 1)
						locker.resourceLocks.Remove(resource);
					else {
						wrapper.counter--;
						last = false;
					}
				}
				if(!last) {
					lock(wrapper.instanceLock) {
						wrapper.taken = false;
						Monitor.Pulse(wrapper.instanceLock);
					}
				}
			}
		}

		public ResourcesLocker()
		{
		}

		public Resource Lock(string resource)
		{
			LockWrapper wrapper = null;
			bool own = false;
			lock(instanceLock) {
				if(resourceLocks.ContainsKey(resource)) {
					wrapper = resourceLocks[resource];
					wrapper.counter++;
				}
				else {
					wrapper = new LockWrapper();
					wrapper.instanceLock = new object();
					wrapper.counter = 1;
					wrapper.taken = true;
					own = true;
					resourceLocks[resource] = wrapper;
				}
			}
			if(!own) {
				lock(wrapper.instanceLock) {
					while(wrapper.taken) {
						Monitor.Wait(wrapper.instanceLock);
					}
				}
			}
			return new Resource(this, resource);
		}
	}
}

