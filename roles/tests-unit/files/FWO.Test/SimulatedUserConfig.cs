﻿using FWO.Config.Api;
using FWO.Config.Api.Data;

namespace FWO.Test
{
    internal class SimulatedGlobalConfig : GlobalConfig
    {
        public Dictionary<string, string> DummyTranslate = SimulatedUserConfig.DummyTranslate;

        public override string GetText(string key)
        {
            return DummyTranslate[key];
        }

        public SimulatedGlobalConfig() : base()
        {
            LangDict = new(){ { "English", DummyTranslate } };
        }
    }

    internal class SimulatedUserConfig : UserConfig
    {
        public static Dictionary<string, string> DummyTranslate = new()
        {
            {"Rules","Rules Report"},
            {"ResolvedRules","Rules Report (resolved)"},
            {"ResolvedRulesTech","Rules Report (technical)"},
            {"UnusedRules","Unused Rules Report"},
            {"Recertification","Recertification Report"},
            {"NatRules","NAT Rules Report"},
            {"Changes","Changes Report"},
            {"ResolvedChanges","Changes Report (resolved)"},
            {"ResolvedChangesTech","Changes Report (technical)"},
            {"Connections","Connections Report"},
            {"date_of_config","Time of configuration"},
            {"generated_on","Generated on"},
            {"negated","not"},
            {"users","Users"},
            {"rule_added","Rule added"},
            {"rule_deleted","Rule deleted"},
            {"rule_modified","Rule modified"},
            {"deleted","deleted"},
            {"added","added"},
            {"missing","missing"},
            {"surplus","surplus"},
            {"change_time","Change Time"},
            {"change_type","Change Type"},
            {"number","No."},
            {"name","Name"},
            {"source_zone","Source Zone"},
            {"source","Source"},
            {"destination_zone","Destination Zone"},
            {"destination","Destination"},
            {"services","Services"},
            {"action","Action"},
            {"track","Track"},
            {"enabled","Enabled"},
            {"uid","Uid"},
            {"comment","Comment"},
            {"type","Type"},
            {"ip_address","IP Address"},
            {"members","Members"},
            {"network_objects","Network Objects"},
            {"network_services","Network Services"},
            {"protocol","Protocol"},
            {"port","Port"},
            {"next_recert","Next Recertification Date"},
            {"owner","Owner"},
            {"ip_matches","IP address match"},
            {"last_hit","Last Hit"},
            {"trans_source","Translated Source"},
            {"trans_destination","Translated Destination"},
            {"trans_services","Translated Services"},
            {"from","from"},
            {"until","until"},
            {"C9001","This object was..."},
            {"C9002","This App Server was..."},
            {"is_in_use","Is in use"},
            {"devices","Devices"},
            {"owners","Owners"},
            {"filter","Filter"},
            {"id","Id"},
            {"ip","Ip"},
            {"group","Group"},
            {"host","Host"},
            {"network","Network"},
            {"ip_range","IP Range"},
            {"connections","Connections"},
            {"interfaces","Interfaces"},
            {"own_common_services","Own Common Services"},
            {"global_common_services","Global Common Services"},
            {"func_reason","Functional Reason"},
            {"interface_description","Interface Description"},
            {"published","Published"},
            {"fetch_data","Fetch data"},
            {"new_connection","New Connection"},
            {"new_app_role","New AppRole: "},
            {"update_app_role","Update AppRole: "},
            {"new_svc_grp","New Servicegroup: "},
            {"add_members",": Add Members"},
            {"remove_members",": Remove Members"},
            {"new_app_zone", "New AppZone: " },
            {"update_app_zone", "Update AppZone: " },
            { "E9301", "Template File not found!" },
            { "E9302", "HTML is invalid!" },
            { "tableofcontent", "Table of content" },
            {"objects","Objects"},
            {"all","All"},
            {"report","Report"},
            {"rule", "Rule"},
            {"collapse_all","Collapse All"},
            {"clear_all", "Clear All"},
            {"used_objects", "Used Objects"},
            {"object_fetch", "Object Fetch"},
            {"app_roles", "AppRoles"},
            {"implemented", "Implemented"},
            {"not_implemented", "Not Implemented"},
            {"with_diffs", "With Diffs"},
            {"app_roles_not_implemented", "AppRoles Not Implemented"},
            {"app_roles_with_diffs", "AppRoles With Diffs"},
            {"missing_app_servers", "Missing App Servers"},
            {"surplus_app_servers", "Surplus App Servers"},
            {"VarianceAnalysis", "VarianceAnalysis"},
            {"connections_not_implemented", "Connections not implemented"},
            {"connections_with_diffs", "Connections with Diffs"},
            {"management", "Management"},
            {"gateway", "Gateway"},
            {"U1003", "In this report..."},
            {"fully_modelled", "fullymodelled"},
            {"more", "more"},
            {"AppRules", "App Rules"},
            {"change_rule", "Change Rule"},
            {"delete_rule", "Delete Rule"},
            {"impl_instructions", "Implementation Instructions"},
            {"init_environment", "Init Environment"},
            {"save_request", "Save request"},
            {"U0001", "Input text..."},
            {"on", "on"},
            {"create_rule", "Create Rule"},
            {"create_group", "Create Group"},
            {"modify_group", "Modify Group"},
            {"delete_group", "Delete Group"},
            {"promote_task", "Promote task"},
            {"modify_rule", "Modify Rule"},
            {"remove_rule", "Remove Rule"},
            {"English", "English"},
            {"last_successful", "Last successful"},
        };

        public override string GetText(string key)
        {
            return DummyTranslate[key];
        }

        public static ConfigItem[] GetAsConfigs()
        {
            List <ConfigItem> configs = [];
            foreach (var dictValuePair in DummyTranslate)
            {
                configs.Add(new(){ Key = dictValuePair.Key, Value = dictValuePair.Value, User = 0});
            }
            return [.. configs];
        }
    }
}
