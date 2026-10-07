// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace System.Web.Mvc
{
    public static class ViewEngines
    {
        private static readonly ViewEngineCollection _engines = new ViewEngineCollection
        {
#if !NET10_0_OR_GREATER
            new WebFormViewEngine(),
#endif
            new RazorViewEngine(),
        };

        public static ViewEngineCollection Engines
        {
            get { return _engines; }
        }
    }
}
