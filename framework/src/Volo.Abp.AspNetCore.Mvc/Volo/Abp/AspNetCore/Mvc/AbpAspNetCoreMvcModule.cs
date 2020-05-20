using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Volo.Abp.ApiVersioning;
using Volo.Abp.AspNetCore.Mvc.ApiExploring;
using Volo.Abp.AspNetCore.Mvc.Conventions;
using Volo.Abp.AspNetCore.Mvc.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc.Json;
using Volo.Abp.AspNetCore.Mvc.Localization;
using Volo.Abp.AspNetCore.VirtualFileSystem;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Http;
using Volo.Abp.DynamicProxy;
using Volo.Abp.Http.Modeling;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.UI;

namespace Volo.Abp.AspNetCore.Mvc
{
    [DependsOn(
        typeof(AbpAspNetCoreModule),
        typeof(AbpLocalizationModule),
        typeof(AbpApiVersioningAbstractionsModule),
        typeof(AbpAspNetCoreMvcContractsModule),
        typeof(AbpUiModule)
        )]
    public class AbpAspNetCoreMvcModule : AbpModule
    {
        public override void PreConfigureServices(ServiceConfigurationContext context)
        {
            DynamicProxyIgnoreTypes.Add<ControllerBase>();
            DynamicProxyIgnoreTypes.Add<PageModel>();

            context.Services.AddConventionalRegistrar(new AbpAspNetCoreMvcConventionalRegistrar());
        }

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            Configure<AbpApiDescriptionModelOptions>(options =>
            {
                options.IgnoredInterfaces.AddIfNotContains(typeof(IAsyncActionFilter));
                options.IgnoredInterfaces.AddIfNotContains(typeof(IFilterMetadata));
                options.IgnoredInterfaces.AddIfNotContains(typeof(IActionFilter));
            });

            Configure<AbpRemoteServiceApiDescriptionProviderOptions>(options =>
            {
                var statusCodes = new List<int>
                {
                    (int) HttpStatusCode.Forbidden,
                    (int) HttpStatusCode.Unauthorized,
                    (int) HttpStatusCode.BadRequest,
                    (int) HttpStatusCode.NotFound,
                    (int) HttpStatusCode.NotImplemented,
                    (int) HttpStatusCode.InternalServerError
                };

                options.SupportedResponseTypes.AddIfNotContains(statusCodes.Select(statusCode => new ApiResponseType
                {
                    Type = typeof(RemoteServiceErrorResponse),
                    StatusCode = statusCode
                }));
            });

            context.Services.PostConfigure<AbpAspNetCoreMvcOptions>(options =>
            {
                if (options.MinifyGeneratedScript == null)
                {
                    options.MinifyGeneratedScript = context.Services.GetHostingEnvironment().IsProduction();
                }
            });

            var mvcCoreBuilder = context.Services.AddMvcCore();
            context.Services.ExecutePreConfiguredActions(mvcCoreBuilder);

            var abpMvcDataAnnotationsLocalizationOptions = context.Services
                .ExecutePreConfiguredActions(
                    new AbpMvcDataAnnotationsLocalizationOptions()
                );

            context.Services
                .AddSingleton<IOptions<AbpMvcDataAnnotationsLocalizationOptions>>(
                    new OptionsWrapper<AbpMvcDataAnnotationsLocalizationOptions>(
                        abpMvcDataAnnotationsLocalizationOptions
                    )
                );

            var mvcBuilder = context.Services.AddMvc()
                .AddNewtonsoftJson(options =>
                {
                    options.SerializerSettings.ContractResolver =
                        new AbpMvcJsonContractResolver(context.Services);
                })
                .AddRazorRuntimeCompilation()
                .AddDataAnnotationsLocalization(options =>
                {
                    options.DataAnnotationLocalizerProvider = (type, factory) =>
                    {
                        var resourceType = abpMvcDataAnnotationsLocalizationOptions
                            .AssemblyResources
                            .GetOrDefault(type.Assembly);

                        if (resourceType != null)
                        {
                            return factory.Create(resourceType);
                        }

                        return factory.CreateDefaultOrNull() ??
                               factory.Create(type);
                    };
                })
                .AddViewLocalization(); //TODO: How to configure from the application? Also, consider to move to a UI module since APIs does not care about it.

            Configure<MvcRazorRuntimeCompilationOptions>(options =>
            {
                options.FileProviders.Add(
                    new RazorViewEngineVirtualFileProvider(
                        context.Services.GetSingletonInstance<IObjectAccessor<IServiceProvider>>()
                    )
                );
            });

            context.Services.ExecutePreConfiguredActions(mvcBuilder);

            //TODO: AddViewLocalization by default..?

            context.Services.TryAddSingleton<IActionContextAccessor, ActionContextAccessor>();

            //Use DI to create controllers
            context.Services.Replace(ServiceDescriptor.Transient<IControllerActivator, ServiceBasedControllerActivator>());

            //Use DI to create view components
            context.Services.Replace(ServiceDescriptor.Singleton<IViewComponentActivator, ServiceBasedViewComponentActivator>());

            //Use DI to create razor page
            context.Services.Replace(ServiceDescriptor.Singleton<IPageModelActivatorProvider, ServiceBasedPageModelActivatorProvider>());

            //Add feature providers
            var partManager = context.Services.GetSingletonInstance<ApplicationPartManager>();
            var application = context.Services.GetSingletonInstance<IAbpApplication>();

            partManager.FeatureProviders.Add(new AbpConventionalControllerFeatureProvider(application));
            partManager.ApplicationParts.Add(new AssemblyPart(typeof(AbpAspNetCoreMvcModule).Assembly));

            Configure<MvcOptions>(mvcOptions =>
            {
                mvcOptions.AddAbp(context.Services);
            });

            Configure<AbpEndpointRouterOptions>(options =>
            {
                options.EndpointConfigureActions.Add(context =>
                {
                    context.Endpoints.MapControllerRoute("defaultWithArea", "{area}/{controller=Home}/{action=Index}/{id?}");
                    context.Endpoints.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
                    context.Endpoints.MapRazorPages();
                });
            });
        }

        public override void OnApplicationInitialization(ApplicationInitializationContext context)
        {
            AddApplicationParts(context);
        }

        private static void AddApplicationParts(ApplicationInitializationContext context)
        {
            var partManager = context.ServiceProvider.GetService<ApplicationPartManager>();
            if (partManager == null)
            {
                return;
            }

            //Plugin modules
            var moduleAssemblies = context
                .ServiceProvider
                .GetRequiredService<IModuleContainer>()
                .Modules
                .Where(m => m.IsLoadedAsPlugIn)
                .Select(m => m.Type.Assembly)
                .Distinct();

            AddToApplicationParts(partManager, moduleAssemblies);

            //Controllers for application services
            var controllerAssemblies = context
                .ServiceProvider
                .GetRequiredService<IOptions<AbpAspNetCoreMvcOptions>>()
                .Value
                .ConventionalControllers
                .ConventionalControllerSettings
                .Select(s => s.Assembly)
                .Distinct();

            AddToApplicationParts(partManager, controllerAssemblies);
        }

        private static void AddToApplicationParts(ApplicationPartManager partManager, IEnumerable<Assembly> moduleAssemblies)
        {
            foreach (var moduleAssembly in moduleAssemblies)
            {
                if (partManager.ApplicationParts.OfType<AssemblyPart>().Any(p => p.Assembly == moduleAssembly))
                {
                    continue;
                }

                partManager.ApplicationParts.Add(new AssemblyPart(moduleAssembly));
            }
        }
    }
}
