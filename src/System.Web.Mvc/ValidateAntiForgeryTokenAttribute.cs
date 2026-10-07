// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
#if !NET10_0_OR_GREATER
using System.Web.Helpers;
#endif

namespace System.Web.Mvc
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class ValidateAntiForgeryTokenAttribute : FilterAttribute, IAuthorizationFilter
    {
        private string _salt;
#if NET10_0_OR_GREATER
        private readonly bool _nativeValidation;
#endif

        public ValidateAntiForgeryTokenAttribute()
#if NET10_0_OR_GREATER
            : this(() => { throw new PlatformNotSupportedException("Native antiforgery requires the filter request context."); })
#else
            : this(AntiForgery.Validate)
#endif
        {
#if NET10_0_OR_GREATER
            _nativeValidation = true;
#endif
        }

        internal ValidateAntiForgeryTokenAttribute(Action validateAction)
        {
            Debug.Assert(validateAction != null);
            ValidateAction = validateAction;
        }

        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "AdditionalDataProvider", Justification = "API name.")]
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "AntiForgeryConfig", Justification = "API name.")]
        [Obsolete("The 'Salt' property is deprecated. To specify custom data to be embedded within the token, use the static AntiForgeryConfig.AdditionalDataProvider property.", error: true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public string Salt
        {
            get { return _salt; }
            set
            {
                if (!String.IsNullOrEmpty(value))
                {
                    throw new NotSupportedException("The 'Salt' property is deprecated. To specify custom data to be embedded within the token, use the static AntiForgeryConfig.AdditionalDataProvider property.");
                }
                _salt = value;
            }
        }

        internal Action ValidateAction { get; private set; }

        public void OnAuthorization(AuthorizationContext filterContext)
        {
            if (filterContext == null)
            {
                throw new ArgumentNullException("filterContext");
            }

#if NET10_0_OR_GREATER
            if (_nativeValidation)
            {
                AspNetWebStack.Native.NativeAntiforgery.Validate(filterContext.HttpContext);
                return;
            }
#endif
            ValidateAction();
        }
    }
}
