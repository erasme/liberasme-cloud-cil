// Cache.cs
// 
//  Helper class to handle items in cache
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
using System.Data;
using System.Threading;
using System.Collections.Generic;
using Mono.Data.Sqlite;

namespace Erasme.Cloud.Cache
{
	public class Cache
	{
		public delegate string GetValidityHandler(string key);
		
		public delegate string CacheMissedHandler(string key, out string validity);
		
		string basePath;
		object lockInstance = new object();
		Dictionary<string,ItemLock> inprogress = new Dictionary<string, ItemLock>();
		CacheMissedHandler handler;
		long timeout;
		
		public class ItemLock
		{
			public bool Done = false;
			
			public string File = null;
			
			public ItemLock()
			{
			}
		}
		
		public Cache(string basePath, long timeout, CacheMissedHandler handler)
		{
			this.basePath = basePath;
			this.timeout = timeout;
			this.handler = handler;
			
			if(!Directory.Exists(basePath))
				Directory.CreateDirectory(basePath);
			
			if(!Directory.Exists(basePath+"/data"))
				Directory.CreateDirectory(basePath+"/data");
			
			if(!File.Exists(basePath+"cache.db")) {
				string connectionString = "URI=file:"+basePath+"cache.db";
				
				using(IDbConnection dbcon = (IDbConnection)new SqliteConnection(connectionString)) {
					dbcon.Open();
					// create the item table
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.CommandText = "CREATE TABLE item (id INTEGER PRIMARY KEY AUTOINCREMENT, key VARCHAR, validity VARCHAR, last INTEGER)";
						dbcmd.ExecuteNonQuery();
					}
					dbcon.Close();
				}
			}
		}

		void Clean()
		{
			string connectionString = "URI=file:"+basePath+"cache.db";
			using(IDbConnection dbcon = (IDbConnection)new SqliteConnection(connectionString)) {
				dbcon.Open();
				
				long id = -1;
				do {
					using(IDbTransaction transaction = dbcon.BeginTransaction()) {
						id = -1;
						// get the item
						using(IDbCommand dbcmd = dbcon.CreateCommand()) {
							dbcmd.Transaction = transaction;
							dbcmd.CommandText = "SELECT id FROM item WHERE last < DATETIME('now', '-"+(timeout+10)+" seconds') LIMIT 1";
							object res = dbcmd.ExecuteScalar();
							if(res != null)
								id = Convert.ToInt64(res);
						}
						if(id != -1) {
							using(IDbCommand dbcmd = dbcon.CreateCommand()) {
								dbcmd.CommandText = "DELETE FROM item WHERE id="+id.ToString();
								dbcmd.Transaction = transaction;
								dbcmd.ExecuteNonQuery();
							}
							File.Delete(basePath+"/data/"+id.ToString());
						}
						transaction.Commit();
					}
				} while(id != -1);
				dbcon.Close();
			}
		}
		
		public string GetItem(string key)
		{
			//Random rand = new Random();
			//bool clean = (rand.Next(100) == 1);
			//if(clean)
			Clean();
			
			ItemLock itemLock = null;
			lock(lockInstance) {
				if(inprogress.ContainsKey(key)) {
					itemLock = inprogress[key];
				}
				else {
					itemLock = new ItemLock();
					inprogress[key] = itemLock;
				}
			}
			long id = -1;
			try {
				lock(itemLock) {
					if(!itemLock.Done) {
						string connectionString = "URI=file:" + basePath + "cache.db";
						using(IDbConnection dbcon = (IDbConnection)new SqliteConnection(connectionString)) {
							dbcon.Open();
							// get the item
							using(IDbCommand dbcmd = dbcon.CreateCommand()) {
								dbcmd.CommandText = "SELECT id FROM item WHERE key='" + key.Replace("'", "''") + "' AND last > DATETIME('now', '-" + timeout + " seconds')";
								object res = dbcmd.ExecuteScalar();
								if(res != null)
									id = Convert.ToInt64(res);
							}
							// if not found, get a new version
							if(id == -1) {
								string validity;
								string file = handler(key, out validity);

								if(file != null) {
									using(IDbCommand dbcmd = dbcon.CreateCommand()) {
										dbcmd.CommandText = "INSERT INTO item (key,validity,last) VALUES ('" + key.Replace("'", "''") + "','" + validity.Replace("'", "''") + "',DATETIME('now'))";
										dbcmd.ExecuteNonQuery();
									}
									// get the insert id
									using(IDbCommand dbcmd = dbcon.CreateCommand()) {
										dbcmd.CommandText = "SELECT last_insert_rowid()";
										id = Convert.ToInt64(dbcmd.ExecuteScalar());
									}
									File.Move(file, basePath + "/data/" + id.ToString());
								}
							}
							dbcon.Close();
						}
						if(id != -1)
							itemLock.File = basePath + "/data/" + id.ToString();
						else
							itemLock.File = null;
					}
				}
			}
			catch(Exception) {
			}
			finally {
				lock(lockInstance) {
					if(inprogress.ContainsKey(key))
						inprogress.Remove(key);
				}
			}
			return itemLock.File;
		}
	}
}

