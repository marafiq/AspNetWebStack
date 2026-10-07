// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Globalization;
#if !NET10_0_OR_GREATER
using System.Web.Helpers;
#endif

namespace System.Web.Mvc
{
    public sealed class QueryStringValueProvider : NameValueCollectionValueProvider
    {
        // QueryString should use the invariant culture since it's part of the URL, and the URL should be
        // interpreted in a uniform fashion regardless of the origin of a particular request.
        public QueryStringValueProvider(ControllerContext controllerContext)
#if NET10_0_OR_GREATER
            : this(controllerContext, AspNetWebStack.Native.NativeQueryValues.Resolve(controllerContext.HttpContext.Request))
#else
            : this(controllerContext, new UnvalidatedRequestValuesWrapper(controllerContext.HttpContext.Request.Unvalidated))
#endif
        {
        }

        // For unit testing
        internal QueryStringValueProvider(ControllerContext controllerContext, IUnvalidatedRequestValues unvalidatedValues)
#if NET10_0_OR_GREATER
            : base(AspNetWebStack.Native.NativeQueryValues.Validated(controllerContext.HttpContext.Request), unvalidatedValues.QueryString, CultureInfo.InvariantCulture)
#else
            : base(controllerContext.HttpContext.Request.QueryString, unvalidatedValues.QueryString, CultureInfo.InvariantCulture)
#endif
        {
        }
    }
}
