using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using FWO.Api.Client.Queries;
using FWO.Data;
using FWO.Data.Generic;
using FWO.Middleware.Server;
using FWO.Services.Generic;
using GraphQL.Client.Http;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;

namespace FWO.Test
{
    /// <summary>
    /// Covers the restriction of generic recertification to device types per owner responsible type (#5788):
    /// maintaining the assignments, their display and the JWT claim derived from them.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    internal class GenericRecertScopeTest
    {
        private const string kDevTypesClaim = "x-hasura-generic-recert-dev-types";

        private sealed class ScopeApiConnection : SimulatedApiConnection
        {
            public List<GenericRecertDevType> Assignments { get; set; } = [];
            public List<(string Query, object? Variables)> Calls { get; } = [];

            public override Task<QueryResponseType> SendQueryAsync<QueryResponseType>(string query, object? variables = null, string? operationName = null, FWO.Api.Client.QueryChunkingOptions? chunkingOptions = null)
            {
                Calls.Add((query, variables));
                object result = query == GenericQueries.getGenericRecertDevTypes ? Assignments : new object();
                return Task.FromResult((QueryResponseType)result);
            }
        }

        [Test]
        public async Task GetDevTypesByResponsibleType_GroupsAndSorts()
        {
            ScopeApiConnection api = new()
            {
                Assignments = [new() { OwnerResponsibleTypeId = 1, DevTypId = 40 }, new() { OwnerResponsibleTypeId = 1, DevTypId = 33 }, new() { OwnerResponsibleTypeId = 2, DevTypId = 33 }]
            };

            Dictionary<int, List<int>> devTypes = await new GenericRecertScopeService(api).GetDevTypesByResponsibleType();

            Assert.That(devTypes[1], Is.EqualTo([33, 40]));
            Assert.That(devTypes[2], Is.EqualTo([33]));
        }

        [Test]
        public async Task SetDevTypes_ReplacesAssignmentsOfTheResponsibleType()
        {
            ScopeApiConnection api = new();

            await new GenericRecertScopeService(api).SetDevTypes(5, [33, 33, 40]);

            (string query, object? variables) = api.Calls.Single();
            List<object> devTypes = (List<object>)variables!.GetType().GetProperty("devTypes")!.GetValue(variables)!;
            Assert.Multiple(() =>
            {
                Assert.That(query, Is.EqualTo(GenericQueries.setGenericRecertDevTypes));
                Assert.That(variables.GetType().GetProperty("ownerResponsibleTypeId")!.GetValue(variables), Is.EqualTo(5));
                Assert.That(devTypes, Has.Count.EqualTo(2));
                Assert.That(devTypes[0].GetType().GetProperty("owner_responsible_type_id")!.GetValue(devTypes[0]), Is.EqualTo(5));
            });
        }

        [Test]
        public void DisplayDevTypes_UsesDeviceTypeNamesOrIds()
        {
            List<GenericKeyConfig> keyConfigs =
            [
                new() { DevTypId = 33, DeviceType = new DeviceType { Name = "Skyhigh Web Gateway", Version = "12ff" } },
                new() { DevTypId = 40 }
            ];

            Assert.That(GenericRecertScopeService.DisplayDevTypes([33, 40, 99], keyConfigs), Is.EqualTo("Skyhigh Web Gateway 12ff, 40, 99"));
            Assert.That(GenericRecertScopeService.DisplayDevTypes([], keyConfigs), Is.Empty);
        }

        [TestCase(new int[0], "{}")]
        [TestCase(new[] { 33, 40 }, "{33,40}")]
        public void Jwt_AlwaysCarriesGenericRecertDevTypes(int[] devTypIds, string expectedClaim)
        {
            using RSA rsa = RSA.Create(2048);
            JwtWriter writer = new(new RsaSecurityKey(rsa));
            UiUser user = new() { Name = "user", Dn = "cn=user", Roles = ["recertifier"], GenericRecertDevTypeIds = [.. devTypIds] };

            string jwt = writer.CreateJWT(user, TimeSpan.FromMinutes(5));

            JwtSecurityToken token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
            Assert.That(token.Claims.Single(claim => claim.Type == kDevTypesClaim).Value, Is.EqualTo(expectedClaim));
        }
    }
}
