// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace System.Web.Mvc
{
    public static class HttpRequestExtensions
    {
        internal const string XHttpMethodOverrideKey = "X-HTTP-Method-Override";

        public static string GetHttpMethodOverride(this HttpRequestBase request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            string incomingVerb = request.HttpMethod;

            if (!String.Equals(incomingVerb, "POST", StringComparison.OrdinalIgnoreCase))
            {
                return incomingVerb;
            }

            string verbOverride = null;
            string headerOverrideValue = request.Headers[XHttpMethodOverrideKey];
            if (!String.IsNullOrEmpty(headerOverrideValue))
            {
                verbOverride = headerOverrideValue;
            }
            else
            {
#if NET10_0_OR_GREATER
                string formOverrideValue = (request is AspNetWebStack.Native.INativeFormRequest form && form.HasFormInput ? form.ValidatedForm : request.Form)[XHttpMethodOverrideKey];
#else
                string formOverrideValue = request.Form[XHttpMethodOverrideKey];
#endif
                if (!String.IsNullOrEmpty(formOverrideValue))
                {
                    verbOverride = formOverrideValue;
                }
                else
                {
#if NET10_0_OR_GREATER
                    string queryStringOverrideValue = (request is AspNetWebStack.Native.INativeQueryRequest query ? query.ValidatedQueryString : request.QueryString)[XHttpMethodOverrideKey];
#else
                    string queryStringOverrideValue = request.QueryString[XHttpMethodOverrideKey];
#endif
                    if (!String.IsNullOrEmpty(queryStringOverrideValue))
                    {
                        verbOverride = queryStringOverrideValue;
                    }
                }
            }
            if (verbOverride != null)
            {
                if (!String.Equals(verbOverride, "GET", StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals(verbOverride, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    incomingVerb = verbOverride;
                }
            }
            return incomingVerb;
        }
    }
}
