﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using IronBoard.Core.Model;
using IronBoard.Core.Model.Exceptions;
using SharpSvn;

namespace IronBoard.Core.Application
{
   public sealed class SvnRepository : CommandLineRepository
   {
      private readonly string _workingCopyPath;
      private SvnClient _svn;
      private SvnUriTarget _root;
      private string _relativeRoot;
      private Uri _repositoryUri;

      public SvnRepository(string workingCopyPath) : base(workingCopyPath, "svn")
      {
         if (workingCopyPath == null) throw new ArgumentNullException("workingCopyPath");
         _workingCopyPath = workingCopyPath;

         Initialize();
      }

      public Uri RemoteRepositoryUri { get { return _repositoryUri; } }

      public override string MainBranchName { get { return "trunk"; } }

      public override string RelativeRoot { get { return _relativeRoot; } }

      public override string ClientVersion
      {
         get { return Exec("--version"); }
      }

      public override string Branch
      {
         get
         {
            if (_repositoryUri != null)
            {
               string s = _repositoryUri.ToString();
               int idx = s.LastIndexOf("branch");
               if (idx != -1)
               {
                  idx = s.IndexOf('/', idx);
                  if (idx != -1)
                  {
                     string branchName = s.Substring(idx + 1);
                     return branchName;
                  }
               }
            }

            return "trunk";
         }
         set
         {
            throw new VersionControlException("switching branches not supported");
         }
      }

      private static readonly char[] RepositoryTrimChars = {'/', ';'};

      public static string TrimRepositoryUrl(string url)
      {
         if (string.IsNullOrEmpty(url)) return null;

         return url.Trim(RepositoryTrimChars);
      }

      private void Initialize()
      {
         Capabilities = new ScmCapabilities(true);
         _svn = new SvnClient();
         SvnInfoEventArgs args;
         _svn.GetInfo(new SvnPathTarget(_workingCopyPath), out args);
         _root = new SvnUriTarget(args.Uri);
         string root = args.Uri.ToString();
         string repoRoot = args.RepositoryRoot.ToString();
         _relativeRoot = root.Substring(repoRoot.Length - 1);
         _repositoryUri = args.Uri;
      }

      public override string GetDiff(RevisionRange range)
      {
         long fromRev = long.Parse(range.From) - 1;   // -1 is enough to include From branch
         long toRev = long.Parse(range.To);

         string diffText;
         using (var ms = new MemoryStream())
         {
            _svn.Diff(
               _root,
               new SvnRevisionRange(fromRev, toRev),
               ms);

            ms.Position = 0;
            diffText = Encoding.UTF8.GetString(ms.ToArray());
         }

         return diffText;
      }

      private WorkItem ToWorkItem(SvnLogEventArgs logEntry)
      {
         var item = new WorkItem(
            logEntry.Revision.ToString(CultureInfo.InvariantCulture),
            logEntry.Author,
            logEntry.LogMessage,
            logEntry.Time);
         if (logEntry.ChangedPaths != null)
         {
            foreach (SvnChangeItem ci in logEntry.ChangedPaths)
            {
               item.ChangedFilePaths.Add(ci.Path);
            }
         }
         return item;
      }

      public override IEnumerable<WorkItem> GetHistory(int maxRevisions)
      {
         var args = new SvnLogArgs {Limit = maxRevisions};

         Collection<SvnLogEventArgs> entries;
         _svn.GetLog(_root.Uri, args, out entries);

         if (entries != null && entries.Count > 0)
         {
            return entries.Select(ToWorkItem);
         }

         return null;
      }
   }
}
