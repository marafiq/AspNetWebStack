// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Globalization;

namespace System.Web.Mvc
{
    /// <summary>
    /// The JQuery Form Value provider is used to handle JQuery formatted data in
    /// request Forms.
    /// </summary>
    public class JQueryFormValueProvider : NameValueCollectionValueProvider
    {
        /// <summary>
        /// Constructs a new instance of the JQuery form ValueProvider
        /// </summary>
        /// <param name="controllerContext">The context on which the ValueProvider operates.</param>
        public JQueryFormValueProvider(
                    ControllerContext controllerContext)
#if NET10_0_OR_GREATER
                : this(controllerContext, AspNetWebStack.Native.NativeFormValues.Resolve(controllerContext.HttpContext.Request))
#else
                : this(controllerContext,
                        new UnvalidatedRequestValuesWrapper(
                                controllerContext.HttpContext.Request.Unvalidated))
#endif
        {
        }

        // For unit testing
        internal JQueryFormValueProvider(
                        ControllerContext controllerContext,
                        IUnvalidatedRequestValues unvalidatedValues)
#if NET10_0_OR_GREATER
            : base(AspNetWebStack.Native.NativeFormValues.Validated(controllerContext.HttpContext.Request), unvalidatedValues.Form, CultureInfo.CurrentCulture, jQueryToMvcRequestNormalizationRequired: true)
#else
            : base(controllerContext.HttpContext.Request.Form, 
                        unvalidatedValues.Form,
                        CultureInfo.CurrentCulture,
                        jQueryToMvcRequestNormalizationRequired: true)
#endif
        {
        }
    }
}
