﻿namespace IronBoard.Core.Model
{
   public class CoreSettings
   {
      public string AuthCookie { get; set; }

      public string UserName { get; set; }

      public string Password { get; set; }

      public int MaxRevisions { get; set; }

      public string TargetUsers { get; set; }

      public string TargetGroups { get; set; }
   }
}
