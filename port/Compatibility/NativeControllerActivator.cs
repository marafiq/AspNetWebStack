using System;
using System.Web.Mvc;
using System.Web.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetWebStack.Native
{
    // The caller lends a request-scoped provider. MVC owns the new controller;
    // the native scope owns its constructor dependencies and outlives release.
    public sealed class NativeControllerActivator : IControllerActivator
    {
        private readonly IServiceProvider _services;
        private readonly Action<IController> _configure;

        public NativeControllerActivator(IServiceProvider services, Action<IController> configure = null)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _configure = configure;
        }

        public IController Create(RequestContext requestContext, Type controllerType)
        {
            ArgumentNullException.ThrowIfNull(requestContext);
            ArgumentNullException.ThrowIfNull(controllerType);
            if (!typeof(IController).IsAssignableFrom(controllerType))
                throw new ArgumentException("The activated type must implement IController.", nameof(controllerType));
            // Do not resolve a registered controller from DI: its scope would
            // then dispose the same instance again after MVC ReleaseController.
            var controller = (IController)ActivatorUtilities.CreateInstance(_services, controllerType);
            try
            {
                _configure?.Invoke(controller);
                return controller;
            }
            catch (Exception configurationError)
            {
                try { (controller as IDisposable)?.Dispose(); }
                catch (Exception disposalError) { throw new AggregateException(configurationError, disposalError); }
                throw;
            }
        }
    }
}
