// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#if !NET10_0_OR_GREATER
using System.Web.Helpers;
#endif

namespace System.Web.Mvc
{
    public sealed class FormValueProviderFactory : ValueProviderFactory
    {
        private readonly UnvalidatedRequestValuesAccessor _unvalidatedValuesAccessor;

        public FormValueProviderFactory()
            : this(null)
        {
        }

        // For unit testing
        internal FormValueProviderFactory(UnvalidatedRequestValuesAccessor unvalidatedValuesAccessor)
        {
#if NET10_0_OR_GREATER
            _unvalidatedValuesAccessor = unvalidatedValuesAccessor ?? (cc => AspNetWebStack.Native.NativeFormValues.Resolve(cc.HttpContext.Request));
#else
            _unvalidatedValuesAccessor = unvalidatedValuesAccessor ?? (cc => new UnvalidatedRequestValuesWrapper(cc.HttpContext.Request.Unvalidated));
#endif
        }

        public override IValueProvider GetValueProvider(ControllerContext controllerContext)
        {
            if (controllerContext == null)
            {
                throw new ArgumentNullException("controllerContext");
            }

#if NET10_0_OR_GREATER
            if (!AspNetWebStack.Native.NativeFormValues.Available(controllerContext.HttpContext.Request)) return null;
#endif
            return new FormValueProvider(controllerContext, _unvalidatedValuesAccessor(controllerContext));
        }
    }
}
