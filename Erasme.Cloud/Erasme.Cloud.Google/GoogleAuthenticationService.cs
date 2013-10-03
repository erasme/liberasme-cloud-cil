// GoogleAuthenticationService.cs
// 
//  Helper service to provide Google OAuth2 authentication
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

// 
// Get the token:
// POST https://accounts.google.com/o/oauth2/token
//  code => 
//  client_id => 
//  client_secret => 
//	redirect_uri => 
//	grant_type => authorization_code
// 
// Get the token info:
// https://www.googleapis.com/oauth2/v1/userinfo?alt=json&access_token=youraccess_token
//

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Erasme.Http;
using Erasme.Json;
using Erasme.Cloud;

namespace Erasme.Cloud.Google
{
	public delegate void GotUserProfileHandler(JsonValue token, JsonValue profile, HttpContext context, string state);
	
	public class GoogleAuthenticationService: HttpHandler
	{
		string ClientId;
		string ClientSecret;
		string RedirectUri;
		GotUserProfileHandler Handler;
		
		object instanceLock = new object();
		Dictionary<string,JsonValue> tokenCache = new Dictionary<string, JsonValue>();
		
		public GoogleAuthenticationService(string clientId, string clientSecret, string redirectUri, GotUserProfileHandler handler)
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
			JsonValue token = null;
			using(WebRequest request = new WebRequest("https://accounts.google.com/o/oauth2/token", allowAutoRedirect: true)) {
				request.Method = "POST";
				request.Headers["content-type"] = "application/x-www-form-urlencoded";
				request.Content = new StringContent(
					"code=" + HttpUtility.UrlEncode(code) + "&" +
					"client_id=" + HttpUtility.UrlEncode(ClientId) + "&" +
					"client_secret=" + HttpUtility.UrlEncode(ClientSecret) + "&" +
					"redirect_uri=" + HttpUtility.UrlEncode(RedirectUri) + "&" +
					"grant_type=authorization_code");
				HttpClientResponse response = request.GetResponse();
				if(response.StatusCode == 200)
					token = response.ReadAsJson();
			}
			return token;
		}
		
		JsonValue GetUserProfile(JsonValue token)
		{
			// get the user info
			JsonValue userProfile = null;
			using(WebRequest request = new WebRequest("https://www.googleapis.com/oauth2/v1/userinfo?alt=json&access_token="+HttpUtility.UrlEncode((string)token["access_token"]), allowAutoRedirect: true)) {
				HttpClientResponse response = request.GetResponse();
				if(response.StatusCode == 200)
					userProfile = response.ReadAsJson();
			}
			return userProfile;
		}
		
		public override void ProcessRequest(HttpContext context)
		{
			if(context.Request.Method == "GET") {
				string state = "";
				if(context.Request.QueryString.ContainsKey("state"))
					state = context.Request.QueryString["state"];

				if(context.Request.Path == "/redirect") {
					context.Response.StatusCode = 307;
					context.Response.Headers["location"] = "https://accounts.google.com/o/oauth2/auth?response_type=code&client_id="+ClientId+"&redirect_uri="+HttpUtility.UrlEncode(RedirectUri)+"&scope="+HttpUtility.UrlEncode("https://www.googleapis.com/auth/userinfo.profile")+"&state="+HttpUtility.UrlEncode(state);
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
