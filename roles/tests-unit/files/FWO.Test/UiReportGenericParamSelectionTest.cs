using Bunit;
using FWO.Api.Client;
using FWO.Config.Api;
using FWO.Data.Generic;
using FWO.Data.Report;
using FWO.Ui.Pages.Reporting;
using FWO.Ui.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace FWO.Test
{
    [TestFixture]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    internal class UiReportGenericParamSelectionTest
    {
        [Test]
        public async Task SelectingManagementsUpdatesTheFilter()
        {
            await using BunitContext context = new();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddScoped<DomEventService>();
            context.Services.AddSingleton<ApiConnection>(new SimulatedGenericApiConnection
            {
                KeyConfigs = [new GenericKeyConfig { DevTypId = 33 }],
                Managements = [new GenericItemManagement { Id = 1, Name = "proxy-1", DevTypId = 33 }, new GenericItemManagement { Id = 2, Name = "proxy-2", DevTypId = 33 }]
            });
            context.Services.AddSingleton<UserConfig>(new SimulatedUserConfig());
            GenericFilter filter = new() { ManagementIds = [2] };
            GenericFilter? changedFilter = null;

            IRenderedComponent<ReportGenericParamSelection> component = context.Render<ReportGenericParamSelection>(parameters => parameters
                .Add(p => p.GenericFilter, filter)
                .Add(p => p.GenericFilterChanged, EventCallback.Factory.Create<GenericFilter>(this, value => changedFilter = value)));

            component.WaitForAssertion(() => Assert.That(component.Markup, Does.Contain("proxy-2")));
            System.Reflection.MethodInfo method = typeof(ReportGenericParamSelection).GetMethod("ManagementsChanged",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            await component.InvokeAsync(() => (Task)method.Invoke(component.Instance, [new List<GenericItemManagement> { new() { Id = 1 } }])!);

            Assert.That(changedFilter?.ManagementIds, Is.EqualTo([1]));
        }

        [Test]
        public async Task ApiErrorsAreReported()
        {
            await using BunitContext context = new();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddScoped<DomEventService>();
            context.Services.AddSingleton<ApiConnection>(new SimulatedApiConnection());
            context.Services.AddSingleton<UserConfig>(new SimulatedUserConfig());
            List<Exception?> errors = [];
            Action<Exception?, string, string, bool> displayMessage = (exception, _, _, _) => errors.Add(exception);

            context.Render<CascadingValue<Action<Exception?, string, string, bool>>>(parameters => parameters
                .Add(p => p.Value, displayMessage)
                .AddChildContent<ReportGenericParamSelection>());

            Assert.That(errors, Has.Count.EqualTo(1));
        }
    }
}
