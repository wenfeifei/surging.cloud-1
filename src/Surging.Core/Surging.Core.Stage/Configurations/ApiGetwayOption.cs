﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Surging.Core.Stage.Configurations
{
   public class ApiGetwayOption
    {
        public  string CacheMode{get;set; }

        public string AuthenticationServiceKey { get; set; }

        public string AuthenticationRoutePath { get; set; }

        public string AuthorizationServiceKey { get; set; }

        public string AuthorizationRoutePath { get; set; }

        public int AccessTokenExpireTimeSpan { get; set; } = 30;

        public string TokenEndpointPath{ get; set; }

        public bool IsUsingTerminal { get; set; } = false;

        public string Terminals { get; set; } = string.Empty;

        public string TokenSecret { get; set; } = string.Empty;

        public int DefaultExpired { get; set; } = 24 * 3;
    }
}
