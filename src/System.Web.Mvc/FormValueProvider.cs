// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Globalization;
#if !NET10_0_OR_GREATER
using System.Web.Helpers;
#endif

namespace System.Web.Mvc
{
    public sealed class FormValueProvider : NameValueCollectionValueProvider
    {
        public FormValueProvider(ControllerContext controllerContext)
#if NET10_0_OR_GREATER
            : this(controllerContext, AspNetWebStack.Native.NativeFormValues.Resolve(controllerContext.HttpContext.Request))
#else
            : this(controllerContext, new UnvalidatedRequestValuesWrapper(controllerContext.HttpContext.Request.Unvalidated))
#endif
        {
        }

        // For unit testing
        internal FormValueProvider(ControllerContext controllerContext, IUnvalidatedRequestValues unvalidatedValues)
#if NET10_0_OR_GREATER
            : base(AspNetWebStack.Native.NativeFormValues.Validated(controllerContext.HttpContext.Request), unvalidatedValues.Form, CultureInfo.CurrentCulture)
#else
            : base(controllerContext.HttpContext.Request.Form, unvalidatedValues.Form, CultureInfo.CurrentCulture)
#endif
        {
        }
    }
}
