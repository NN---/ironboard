﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IronBoard.Core.Model;
using IronBoard.Core.Views;
using IronBoard.RBWebApi.Model;

namespace IronBoard.Core.Presenters
{
   public class WorkItemRangeSelectorPresenter
   {
      private static readonly Regex JiraIssueRegex = new Regex("[a-zA-Z]+-[0-9]+", RegexOptions.Multiline);
      private readonly IWorkItemRangeSelectorView _view;
      private readonly Dictionary<WorkItem, int> _itemOrder = new Dictionary<WorkItem, int>(); 

      public WorkItemRangeSelectorPresenter(IWorkItemRangeSelectorView view)
      {
         _view = view;
      }

      public void ReloadData(int maxItems)
      {
         Task.Factory.StartNew(() =>
         {
            IEnumerable<WorkItem> items = null;
            Exception ex = null;
            try
            {
               items = GetCurrentWorkItems(maxItems);
               _itemOrder.Clear();
               if (items != null)
               {
                  int i = 0;
                  foreach (WorkItem wi in items)
                  {
                     _itemOrder[wi] = i++;
                  }
               }
            }
            catch (Exception ex1)
            {
               ex = ex1;
            }
            _view.UpdateList(ex, items);
         });
      }

      public string ToListString(WorkItem i)
      {
         return string.Format("{0}: {1}@{2}| {3}",
                              i.ItemId, i.Author, i.Time, i.Comment);
      }

      private IEnumerable<WorkItem> GetCurrentWorkItems(int maxItems)
      {
         IEnumerable<WorkItem> items = null;
         if(IbApplication.CodeRepository != null)
         {
            items = IbApplication.CodeRepository.GetHistory(maxItems);
            if (items != null) items = items.OrderBy(i => -int.Parse(i.ItemId)); //order by revision number desc
         }
         return items;
      }

      public Tuple<int, int> GetRange(IEnumerable<WorkItem> items)
      {
         int min = int.MaxValue, max = 0;
         if (items != null)
         {
            foreach (WorkItem wi in items)
            {
               int rev = int.Parse(wi.ItemId);
               if (rev < min) min = rev;
               if (rev > max) max = rev;
            }
         }
         return min <= max ? new Tuple<int, int>(min - 1, max) : null;
      }

      public Tuple<int, int> GetRange(IEnumerable allItems, IEnumerable selectedItems)
      {
         bool skipped;
         return GetRange(SelectContinuousItems(allItems, selectedItems, out skipped));
      }

      public IEnumerable<WorkItem> SelectContinuousItems(IEnumerable allItems, IEnumerable selectedItems, out bool skiped)
      {
         return SelectContinuousItems(allItems.Cast<WorkItem>().ToList(), selectedItems.Cast<WorkItem>().ToList(), out skiped);
      }

      public IEnumerable<WorkItem> SelectContinuousItems(IEnumerable<WorkItem> allItems, IEnumerable<WorkItem> selectedItems, out bool skipped)
      {
         skipped = false;
         var result = new List<WorkItem>();
         var itemToPosition = new Dictionary<WorkItem, int>();
         int t = 0;
         foreach (WorkItem wi in allItems)
         {
            itemToPosition[wi] = t++;
         }
         var selectedSorted = selectedItems.OrderBy(i => itemToPosition[i]);

         int last = -1;
         foreach (WorkItem wi in selectedSorted)
         {
            int pos = itemToPosition[wi];
            if (last == -1)
            {
               last = pos;
               result.Add(wi);
            }
            else if (pos == ++last)
            {
               result.Add(wi);
            }
            else
            {
               skipped = true;
               break;
            }
         }

         return result;
      }

      public string GetCommandLine(Tuple<int, int> range)
      {
         return range == null
                   ? string.Empty
                   : string.Format("post-review --revision-range={0}:{1}", range.Item1, range.Item2);
      }

      public string[] ExtractBugsClosed(IEnumerable<WorkItem> lines)
      {
         var result = new HashSet<string>();
         if (lines != null)
         {
            foreach (WorkItem wi in lines)
            {
               if (wi != null && wi.Comment != null)
               {
                  foreach (Match m in JiraIssueRegex.Matches(wi.Comment))
                  {
                     result.Add(m.ToString().ToUpper());
                  }
               }
            }
         }

         return result.ToArray();
      }

      private string GenerateExcuseForNoTesting()
      {
         var rnd = new Random(DateTime.Now.Millisecond);
         string[] excuses = Strings.ReviewDetails_NoTestingExcuse.Split(new[] { Environment.NewLine },
                                                                        StringSplitOptions.RemoveEmptyEntries);
         return excuses[rnd.Next(excuses.Length)];
      }

      public string ExtractTestingDone(IEnumerable<WorkItem> lines)
      {
         var utFileNames = new HashSet<string>();
         if (lines != null)
         {
            foreach (WorkItem wi in lines)
            {
               foreach (string filePath in wi.ChangedFilePaths)
               {
                  string name = filePath.Substring(filePath.LastIndexOf('/') + 1);
                  if (name.IndexOf("test", StringComparison.InvariantCultureIgnoreCase) != -1 && !name.Contains(".csproj"))
                  {
                     utFileNames.Add(name);
                  }
               }
            }
         }

         if (utFileNames.Count == 0) return GenerateExcuseForNoTesting();

         return string.Format(Strings.ReviewDetails_UnitTestingDone, string.Join(", ", utFileNames));
      }

      public void ExtractBasicMetadata(IEnumerable<WorkItem> selectedItems, Review review)
      {
         if (selectedItems != null)
         {
            List<WorkItem> itemsList = selectedItems.ToList();
            var lines = new List<string>();
            foreach (WorkItem wi in itemsList)
            {
               string comment = wi.Comment == null ? null : wi.Comment.Trim();
               if (!string.IsNullOrEmpty(comment))
               {
                  lines.AddRange(comment.Split(new[] {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries));
               }
            }

            if (lines.Count > 0)
            {
               lines = lines.Distinct().ToList();
               review.Subject = lines[0]
                  .Trim(' ', '*', '-', '+', '=', '\t', '[', ']', '.')
                  .Replace("\r", "").Replace("\n", " ").Replace("  ", " ");
               review.Subject = review.Subject.Substring(0, 1).ToUpper() + review.Subject.Substring(1);
               review.Description = String.Join(Environment.NewLine, lines);
            }

            review.BugsClosed = String.Join(", ", ExtractBugsClosed(itemsList));
            review.TestingDone = ExtractTestingDone(itemsList);
            review.Branch = IbApplication.CodeRepository.Branch;
         }
      }

      public void OpenInBrowser(Review r)
      {
         string url = string.Format("{0}/r/{1}", IbApplication.RbClient.ServerUri, r.Id);
         IbApplication.OpenBrowserWindow(url, false);
      }

      public string GetDetailsTitle()
      {
         return string.Format(Strings.ReviewDetails_NewTicket, IbApplication.CodeRepository.Branch);
      }
   }
}
