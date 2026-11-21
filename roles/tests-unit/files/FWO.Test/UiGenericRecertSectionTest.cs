using System.Reflection;
using Bunit;
using FWO.Api.Client;
using FWO.Api.Client.Queries;
using FWO.Basics;
using FWO.Config.Api;
using FWO.Data;
using FWO.Data.Generic;
using BlazorTable;
using FWO.Ui.Pages;
using FWO.Ui.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace FWO.Test
{
    [TestFixture]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    internal class UiGenericRecertSectionTest
    {
        private const string kPayload = "<b>x</b>\" onmouseover=\"x'";
        private static readonly GenericItemManagement kProxy = new() { Id = 1, Name = "proxy-1", DevTypId = 33 };

        private readonly List<(Exception? Exception, string Title, string Text)> messages = [];

        private static SimulatedGenericApiConnection CreateApi(bool withManagements = true)
        {
            return new SimulatedGenericApiConnection
            {
                KeyConfigs = [new GenericKeyConfig { DevTypId = 33, UidKey = "uid", ReportableFields = ["action"] }],
                Managements = withManagements ? [kProxy] : [],
                Items =
                [
                    new GenericConfigItem { Id = 1, MgmId = 1, ItemUid = "own", ItemName = kPayload, OwnerId = 7, FirstSeen = DateTime.Now.AddDays(-400), Management = kProxy },
                    new GenericConfigItem { Id = 2, MgmId = 1, ItemUid = "foreign", OwnerId = 8, FirstSeen = DateTime.Now.AddDays(-400), Management = kProxy },
                    new GenericConfigItem { Id = 3, MgmId = 1, ItemUid = "notdue", OwnerId = 7, FirstSeen = DateTime.Now, Management = kProxy }
                ]
            };
        }

        private IRenderedComponent<GenericRecertSection> Render(BunitContext context, SimulatedGenericApiConnection api, string role, FwoOwner? owner = null, bool commentRequired = false)
        {
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddBlazorTable();
            context.Services.AddScoped<DomEventService>();
            context.Services.AddSingleton<ApiConnection>(api);
            SimulatedUserConfig userConfig = new() { InitialRecertificationPeriod = 30, RecertificationPeriod = 365, CommentRequired = commentRequired };
            userConfig.User.Roles = [role];
            userConfig.User.Dn = "cn=me";
            context.Services.AddSingleton<UserConfig>(userConfig);
            Action<Exception?, string, string, bool> displayMessage = (exception, title, text, _) => messages.Add((exception, title, text));
            IRenderedComponent<CascadingValue<Action<Exception?, string, string, bool>>> wrapper =
                context.Render<CascadingValue<Action<Exception?, string, string, bool>>>(parameters => parameters
                    .Add(p => p.Value, displayMessage)
                    .AddChildContent<GenericRecertSection>(child => child
                        .Add(p => p.SelectedOwner, owner)
                        .Add(p => p.RecertifiableOwnerIds, [7])
                        .Add(p => p.ReadableOwnerIds, [7, 8])));
            return wrapper.FindComponent<GenericRecertSection>();
        }

        private static async Task InvokePrivate(IRenderedComponent<GenericRecertSection> section, string method, params object[] args)
        {
            MethodInfo methodInfo = typeof(GenericRecertSection).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!;
            await section.InvokeAsync(() => methodInfo.Invoke(section.Instance, args) is Task task ? task : Task.CompletedTask);
        }

        private static void SetField(IRenderedComponent<GenericRecertSection> section, string field, object value)
        {
            typeof(GenericRecertSection).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(section.Instance, value);
        }

        [Test]
        public async Task WithoutGenericManagements_RendersNothing()
        {
            await using BunitContext context = new();
            IRenderedComponent<GenericRecertSection> section = Render(context, CreateApi(false), Roles.Recertifier);

            Assert.That(section.Instance.HasGenericManagements, Is.False);
            Assert.That(section.Markup.Trim(), Does.Not.Contain("<h4"));
        }

        [Test]
        public async Task Load_ShowsDueItemsEncodedAndOnlyOwnItemsAreSelectable()
        {
            await using BunitContext context = new();
            IRenderedComponent<GenericRecertSection> section = Render(context, CreateApi(), Roles.Recertifier);

            await section.InvokeAsync(() => section.Instance.Load(0));

            section.WaitForAssertion(() =>
            {
                Assert.That(section.Markup, Does.Contain("foreign"));
                Assert.That(section.Markup, Does.Not.Contain("notdue"));
                Assert.That(section.Markup, Does.Not.Contain("<b>x</b>"));
                Assert.That(section.Find("#recert1").HasAttribute("disabled"), Is.False);
                Assert.That(section.Find("#recert2").HasAttribute("disabled"), Is.True);
            });
        }

        [Test]
        public async Task Execute_WritesOnlyDecisionsTheUserMayMake()
        {
            await using BunitContext context = new();
            SimulatedGenericApiConnection api = CreateApi();
            IRenderedComponent<GenericRecertSection> section = Render(context, api, Roles.Recertifier);
            await section.InvokeAsync(() => section.Instance.Load(0));
            section.Find("#recert1").Change(true);
            section.Find("#decert2").Change(true);

            await InvokePrivate(section, "OpenCommentPopup", false);
            await InvokePrivate(section, "Execute");

            object[] decisions = SimulatedGenericApiConnection.Read<object[]>(api.VariablesOf(GenericQueries.addGenericRecertifications).Single(), "recertifications");
            Assert.That(decisions, Has.Length.EqualTo(1));
            Assert.That(SimulatedGenericApiConnection.Read<long>(decisions[0], "config_item_id"), Is.EqualTo(1));
        }

        [Test]
        public async Task Execute_RecertifiesTheSelectedOwner()
        {
            await using BunitContext context = new();
            SimulatedGenericApiConnection api = CreateApi();
            IRenderedComponent<GenericRecertSection> section = Render(context, api, Roles.Recertifier, new FwoOwner { Id = 7, Name = "App", RecertInterval = 30 });

            await InvokePrivate(section, "OpenCommentPopup", true);
            await InvokePrivate(section, "Execute");

            Assert.That(api.VariablesOf(GenericQueries.addGenericOwnerRecertification), Has.Count.EqualTo(1));
            object[] decisions = SimulatedGenericApiConnection.Read<object[]>(api.VariablesOf(GenericQueries.addGenericRecertifications).Single(), "recertifications");
            Assert.That(decisions.Select(decision => SimulatedGenericApiConnection.Read<long>(decision, "config_item_id")), Is.EquivalentTo([1L, 3L]));
        }

        [Test]
        public async Task Execute_WithoutRequiredCommentWritesNothing()
        {
            await using BunitContext context = new();
            SimulatedGenericApiConnection api = CreateApi();
            IRenderedComponent<GenericRecertSection> section = Render(context, api, Roles.Recertifier, commentRequired: true);
            await InvokePrivate(section, "OpenCommentPopup", false);
            SetField(section, "comment", " ");

            await InvokePrivate(section, "Execute");

            Assert.That(api.VariablesOf(GenericQueries.addGenericRecertifications), Is.Empty);
            Assert.That(messages.Select(message => message.Text), Does.Contain(new SimulatedUserConfig().GetText("E4001")));
        }

        [Test]
        public async Task Auditor_SeesItemsWithoutActions()
        {
            await using BunitContext context = new();
            IRenderedComponent<GenericRecertSection> section = Render(context, CreateApi(), Roles.Auditor);

            await section.InvokeAsync(() => section.Instance.Load(0));

            section.WaitForAssertion(() =>
            {
                Assert.That(section.Markup, Does.Contain("foreign"));
                Assert.That(section.FindAll("#recert1"), Is.Empty);
                Assert.That(section.FindAll("button.btn-danger"), Is.Empty);
            });
        }

        [Test]
        public async Task Load_ReportsApiErrors()
        {
            await using BunitContext context = new();
            SimulatedGenericApiConnection api = CreateApi();
            IRenderedComponent<GenericRecertSection> section = Render(context, api, Roles.Recertifier);
            api.Items = null!;

            await section.InvokeAsync(() => section.Instance.Load(0));

            Assert.That(messages.Any(message => message.Exception != null), Is.True);
        }
    }
}
