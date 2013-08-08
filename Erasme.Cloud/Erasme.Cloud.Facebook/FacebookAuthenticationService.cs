// FacebookAuthenticationService.cs
// 
//  Helper service to provide Facebook OAuth2 authentication
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
using System.IO;
using System.Net;
using System.Text;
using System.Collections.Generic;
using Erasme.Http;
using Erasme.Json;
using Erasme.Cloud;

namespace Erasme.Cloud.Facebook
{
	public delegate void GotUserProfileHandler(JsonValue token, JsonValue profile, HttpContext context, string state);
	
	public class FacebookAuthenticationService: HttpHandler
	{
		string ClientId;
		string ClientSecret;
		string RedirectUri;
		GotUserProfileHandler Handler;
		
		object instanceLock = new object();
		Dictionary<string,JsonValue> tokenCache = new Dictionary<string, JsonValue>();
		
		public FacebookAuthenticationService(string clientId, string clientSecret, string redirectUri, GotUserProfileHandler handler)
		{
			ClientId = clientId;
			ClientSecret = clientSecret;
			RedirectUri = redirectUri;
			Handler = handler;
		}
		
		string SaveToken(JsonValue token)
		{
			string id = null;
			lock(instanceLock) {
				do {
					string randchars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
					Random rand = new Random();
					StringBuilder sb = new StringBuilder();
					for(int i = 0; i < 10; i++)
						sb.Append(randchars[rand.Next(randchars.Length)]);
					id = sb.ToString();
				} while(tokenCache.ContainsKey(id));
				
				tokenCache[id] = token;
				token["create_date"] = DateTime.Now.Ticks;
			}
			return id;
		}
		
		JsonValue GetToken(string tokenId)
		{
			JsonValue token = null;
			lock(instanceLock) {
				// clean old token
				List<string> removeList = new List<string>();
				DateTime now = DateTime.Now;
				foreach(string id in tokenCache.Keys) {
					if(now - new DateTime((long)tokenCache[id]["create_date"]) > TimeSpan.FromSeconds((double)tokenCache[id]["expires_id"] - 5))
						removeList.Add(id);
				}
				foreach(string id in removeList)
					tokenCache.Remove(id);
				
				if(tokenCache.ContainsKey(tokenId))
					token = tokenCache[tokenId];
			}
			return token;
		}
		
		JsonValue GetTokenFromCode(string code)
		{
			// get the access token
			// https://developers.facebook.com/docs/howtos/login/server-side-login/				
			System.Net.WebRequest webRequest = System.Net.WebRequest.Create(
				"https://graph.facebook.com/oauth/access_token?"+
				"code="+HttpUtility.UrlEncode(code)+"&"+
				"client_id="+HttpUtility.UrlEncode(ClientId)+"&"+
				"client_secret="+HttpUtility.UrlEncode(ClientSecret)+"&"+
				"redirect_uri="+HttpUtility.UrlEncode(RedirectUri));
			HttpWebRequest httpWebRequest = webRequest as HttpWebRequest;
			if(httpWebRequest != null)
				httpWebRequest.AllowAutoRedirect = true;
			webRequest.Method = "GET";
				
			HttpWebResponse response = webRequest.GetResponse() as HttpWebResponse;
			Stream responseStream = response.GetResponseStream();
			StreamReader reader = new StreamReader(responseStream);

			string paramsString = reader.ReadToEnd();
			string[] tab = paramsString.Split('&');
			JsonValue jsonToken = new JsonObject();
			for(int i = 0; i < tab.Length; i++) {
				string[] tab2 = tab[i].Split('=');
				if(tab2.Length == 2)
					jsonToken[tab2[0]] = tab2[1];
			}
			reader.Close();
			responseStream.Close();
			return jsonToken;
		}
		
		JsonValue GetUserProfile(JsonValue token)
		{			
			// get the user info API: https://developers.facebook.com/docs/reference/api/user/
			System.Net.WebRequest webRequest = System.Net.WebRequest.Create(
				"https://graph.facebook.com/me?access_token="+HttpUtility.UrlEncode((string)token["access_token"])+
				"&fields=picture,first_name,last_name");
			HttpWebRequest httpWebRequest = webRequest as HttpWebRequest;
			if(httpWebRequest != null)
				httpWebRequest.AllowAutoRedirect = true;
			webRequest.Method = "GET";
			HttpWebResponse response = webRequest.GetResponse() as HttpWebResponse;
			Stream responseStream = response.GetResponseStream();
			StreamReader reader = new StreamReader(responseStream);
			string jsonString = reader.ReadToEnd();
			reader.Close();
			responseStream.Close();

			return JsonValue.Parse(jsonString);
		}
		
		public override void ProcessRequest(HttpContext context)
		{
			if(context.Request.Method == "GET") {
				string state = "";
				if(context.Request.QueryString.ContainsKey("state"))
					state = context.Request.QueryString["state"];

				if(context.Request.Path == "/redirect") {
					context.Response.StatusCode = 307;
					// https://developers.facebook.com/docs/reference/dialogs/oauth/
					context.Response.Headers["location"] = "https://www.facebook.com/dialog/oauth?response_type=code&client_id="+ClientId+"&redirect_uri="+HttpUtility.UrlEncode(RedirectUri)+"&scope=email&state="+HttpUtility.UrlEncode(state);
				}
				else if(context.Request.QueryString.ContainsKey("code")) {
					JsonValue token = GetTokenFromCode(context.Request.QueryString["code"]);
					JsonValue profile = GetUserProfile(token);
					Handler(token, profile, context, state);
				}
				else {
					Handler(null, null, context, state);
				}
			}
		}
	}
}

