﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Authentication;
using System.Text;
using IronBoard.RBWebApi.Application;
using IronBoard.RBWebApi.Model;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace IronBoard.RBWebApi
{
   public class RBClient
   {
      private readonly RestClient _client;
      private readonly ReviewBoardRc _config;
      private readonly string _svnRepositoryPath;
      private string _cookie;


      public RBClient(string svnRepositoryPath, string projectRootFolder)
      {
         if (svnRepositoryPath == null) throw new ArgumentNullException("svnRepositoryPath");
         if (projectRootFolder == null) throw new ArgumentNullException("projectRootFolder");

         _svnRepositoryPath = svnRepositoryPath;
         _config = new ReviewBoardRc(projectRootFolder);
         if(_config.Uri == null) throw new ArgumentException("config file doesn't contain ReviewBoard server url");
         _client = new RestClient(new Uri(_config.Uri, "api").ToString());
      }

      public Uri ServerUri { get { return _config.Uri; } }

      private RestRequest CreateRequest(string resource, Method method)
      {
         var request = new RestRequest(resource) {Method = method};
         if(_cookie != null) request.AddCookie("rbsessionid", _cookie);
         return request;
      }

      private RestRequest CreateRequest(Uri uri, Method method)
      {
         if (uri.IsAbsoluteUri)
         {
            string resource = uri.ToString().Substring(_client.BaseUrl.Length);
            return CreateRequest(resource, method);
         }

         return CreateRequest(uri.ToString(), method);
      }

      public IEnumerable<Repository> GetRepositories()
      {
         var result = new List<Repository>();
         var request = new RestRequest("repositories/");
         request.Method = Method.GET;
         RestResponse response = _client.Execute(request) as RestResponse;
         if(response != null)
         {
            JObject jo = JObject.Parse(response.Content);
            JArray repos = jo["repositories"] as JArray;
            foreach(JObject r in repos)
            {
               result.Add(new Repository(
                  r.Value<string>("id"),
                  r.Value<string>("name"),
                  r.Value<string>("tool"),
                  r.Value<string>("path")));
            }
         }
         return result;
      }

      public void Authenticate(NetworkCredential creds)
      {
         var request = CreateRequest("repositories/", Method.GET);
         string auth = creds.UserName + ":" + creds.Password;
         auth = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(auth));
         request.AddHeader(HttpRequestHeader.Authorization.ToString(), auth);
         var response = _client.Execute(request);
         var cookie = response.Cookies.FirstOrDefault(c => c.Name == "rbsessionid");
         if(cookie == null) throw new AuthenticationException("invalid username or password");
         _cookie = cookie.Value;
      }

      private Uri ParseLink(JObject links, string linkName)
      {
         if(links != null && linkName != null)
         {
            JObject linkObj = links[linkName] as JObject;
            if(linkObj != null)
            {
               string href = linkObj.Value<string>("href");
               if(href != null) return new Uri(href);
            }
         }
         return null;
      }

      private void ParseReview(string response, Review review, string entityTag, bool parseData)
      {
         JObject jo = JObject.Parse(response);
         JObject review_request = jo[entityTag] as JObject;
         review.Id = review_request.Value<long>("id");
         if (parseData)
         {
            review.Subject = review_request.Value<string>("summary");
            review.Description = review_request.Value<string>("description");
            review.TestingDone = review_request.Value<string>("testing_done");
         }

         JObject links = review_request["links"] as JObject;
         if(review.Links == null) review.Links = new ReviewLinks();
         review.Links.Diffs = ParseLink(links, "diffs");
         review.Links.Update = ParseLink(links, "update");
         review.Links.Draft = ParseLink(links, "draft");
         review.Links.Self = ParseLink(links, "self");
      }

      /// <summary>
      /// Posts review to RB server
      /// </summary>
      /// <param name="review">The review to be posted. Some fields get updated by values received from server.</param>
      public void Post(Review review)
      {
         var request = CreateRequest("review-requests/", Method.POST);
         request.AddParameter("repository", review.Repository.Path);
         var response = _client.Execute(request) as RestResponse;
         ParseReview(response.Content, review, "review_request", false);

         //I couldn't post extra fields during initial review creation, they have to be sent as an update
         Update(review);
      }

      public void Update(Review review)
      {
         var request = CreateRequest(review.Links.Draft, Method.PUT);
         if (review.Subject != null) request.AddParameter("summary", review.Subject);
         if (review.Description != null) request.AddParameter("description", review.Description);
         if (review.TestingDone != null) request.AddParameter("testing_done", review.TestingDone);
         var response = _client.Execute(request) as RestResponse;
      }

      public void AttachDiff(Review review, string diffText)
      {
         if (review == null) throw new ArgumentNullException("review");
         if (diffText == null) throw new ArgumentNullException("diffText");

         string diffBaseDir = _svnRepositoryPath.Substring(review.Repository.Path.Length);
         if (!diffBaseDir.StartsWith("/")) diffBaseDir = "/" + diffBaseDir;
         if (diffBaseDir.EndsWith("/")) diffBaseDir = diffBaseDir.Substring(0, diffBaseDir.Length - 1);

         var request = CreateRequest(review.Links.Diffs, Method.POST);
         request.AddFile("basedir", Encoding.UTF8.GetBytes(diffBaseDir), null);
         request.AddFile("path", Encoding.UTF8.GetBytes(diffText), "diff");

         var response = _client.Execute(request) as RestResponse;
      }
   }
}