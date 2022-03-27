
-- to delete
-- Create unique index "firewall_akey" on "device" using btree ("mgm_id","dev_id");
-- Create index "kunden_akey" on "tenant" using btree ("tenant_name");
-- Create unique index "management_akey" on "management" using btree ("mgm_name","tenant_id");
-- Create unique index "stm_color_akey" on "stm_color" using btree ("color_name");
-- Create index "stm_fw_typ_a2key" on "stm_dev_typ" using btree ("dev_typ_name");
-- Create unique index "stm_fw_typ_akey" on "stm_dev_typ" using btree ("dev_typ_name","dev_typ_version");
-- Create unique index "stm_obj_typ_akey" on "stm_obj_typ" using btree ("obj_typ_name");
-- Create index "IX_relationship4" on "management" ("tenant_id");
-- Create index "IX_relationship6" on "device" ("dev_typ_id");
-- Create index "IX_Relationship93" on "usr" ("usr_typ_id");
-- Create index "IX_relationship11" on "object" ("obj_nat_install");
-- Create index "IX_relationship7" on "device" ("tenant_id");
-- Create index "IX_Relationship165" on "usr" ("tenant_id");
-- Create index "IX_Relationship188" on "uiuser" ("tenant_id");
-- Create index "IX_relationship10" on "object" ("nattyp_id");
-- Create index "IX_Relationship52" on "usr" ("user_color_id");
-- Create index "IX_Relationship63" on "changelog_object" ("import_admin");
-- Create index "IX_Relationship69" on "changelog_service" ("import_admin");
-- Create index "IX_Relationship70" on "changelog_user" ("import_admin");
-- Create index "IX_Relationship71" on "changelog_rule" ("import_admin");
-- Create index "IX_Relationship109" on "changelog_object" ("doku_admin");
-- Create index "IX_Relationship110" on "changelog_service" ("doku_admin");
-- Create index "IX_Relationship111" on "changelog_user" ("doku_admin");
-- Create index "IX_Relationship112" on "changelog_rule" ("doku_admin");
-- Create index "IX_Relationship159" on "object" ("last_change_admin");
-- Create index "IX_Relationship161" on "rule" ("last_change_admin");
-- Create index "IX_Relationship162" on "service" ("last_change_admin");
-- Create index "IX_Relationship163" on "usr" ("last_change_admin");
-- Create index FwoImportIdx001 on import_rule (rule_id);
-- Create index FwoImportIxd002 on zone (zone_name,mgm_id);
-- Create index FwoImportIdx003 on rule (rule_uid,mgm_id,dev_id,active,nat_rule,xlate_rule);

-- to keep
Create index "IX_Relationship155" on "changelog_object" ("change_type_id");
Create index "IX_Relationship156" on "changelog_service" ("change_type_id");
Create index "IX_Relationship157" on "changelog_user" ("change_type_id");
Create index "IX_Relationship158" on "changelog_rule" ("change_type_id");
Create index "IX_relationship5" on "device" ("mgm_id");
Create index "IX_relationship8" on "object" ("mgm_id");
Create index "IX_relationship21" on "rule" ("mgm_id");
Create index "IX_relationship17" on "service" ("mgm_id");
Create index "IX_Relationship38" on "zone" ("mgm_id");
Create index "IX_Relationship43" on "usr" ("mgm_id");
Create index "IX_Relationship127" on "changelog_rule" ("mgm_id");
Create index "IX_Relationship129" on "changelog_user" ("mgm_id");
Create index "IX_Relationship130" on "changelog_object" ("mgm_id");
Create index "IX_Relationship131" on "changelog_service" ("mgm_id");
Create index "rule_index" on "rule" using btree ("mgm_id","rule_id","rule_uid","dev_id");
Create index "IX_Relationship128" on "changelog_rule" ("dev_id");
Create index "IX_Relationship186" on "rule" ("dev_id");
Create index IF NOT EXISTS idx_import_rule01 on import_rule (rule_id);
Create index IF NOT EXISTS idx_zone01 on zone (zone_name,mgm_id);
Create index IF NOT EXISTS idx_rule01 on rule (rule_uid,mgm_id,dev_id,active,nat_rule,xlate_rule);

-- primary keys?
Create index "IX_relationship25" on "rule_from" ("rule_id");
Create index "IX_relationship29" on "rule_service" ("rule_id");
Create index "IX_relationship27" on "rule_to" ("rule_id");
Create index "IX_relationship30" on "rule_service" ("svc_id");
Create index "IX_relationship19" on "svcgrp" ("svcgrp_id");
Create index "IX_relationship20" on "svcgrp" ("svcgrp_member_id");
Create index "IX_relationship13" on "objgrp" ("objgrp_id");
Create index "IX_relationship14" on "objgrp" ("objgrp_member_id");
Create index "IX_Relationship118" on "svcgrp_flat" ("svcgrp_flat_id");
Create index "IX_Relationship119" on "svcgrp_flat" ("svcgrp_flat_member_id");
Create index "IX_Relationship149" on "usergrp_flat" ("usergrp_flat_id");
Create index "IX_Relationship150" on "usergrp_flat" ("usergrp_flat_member_id");


-- make sure a maximum of one stop_time=null entry exists per mgm_id (only one running import per mgm):
CREATE UNIQUE INDEX import_control_only_one_null_stop_time_per_mgm_when_null ON import_control (mgm_id) WHERE stop_time IS NULL;

-- tbd
Create index "IX_Relationship178" on "zone" ("zone_create");
Create index "IX_Relationship179" on "zone" ("zone_last_seen");
Create unique index "kundennetze_akey" on "tenant_network" using btree ("tenant_net_id","tenant_id");
Create unique index "rule_from_unique_index" on "rule_from" using btree ("rule_id","obj_id","user_id");
Create index "import_control_start_time_idx" on "import_control" using btree ("start_time");

Create index "IX_relationship26" on "rule_from" ("obj_id");
Create index "IX_relationship28" on "rule_to" ("obj_id");
Create index "IX_Relationship65" on "changelog_object" ("old_obj_id");
Create index "IX_Relationship66" on "changelog_object" ("new_obj_id");
Create index "IX_Relationship105" on "objgrp_flat" ("objgrp_flat_id");
Create index "IX_Relationship106" on "objgrp_flat" ("objgrp_flat_member_id");
Create index "IX_Relationship72" on "changelog_rule" ("new_rule_id");
Create index "IX_Relationship73" on "changelog_rule" ("old_rule_id");
Create index "IX_Relationship74" on "changelog_service" ("new_svc_id");
Create index "IX_Relationship75" on "changelog_service" ("old_svc_id");
Create index "IX_relationship23" on "rule" ("action_id");
Create index "IX_relationship12" on "object" ("obj_color_id");
Create index "IX_relationship18" on "service" ("svc_color_id");
Create index "IX_Relationship83" on "management" ("dev_typ_id");
Create index "IX_relationship9" on "object" ("obj_typ_id");
Create index "IX_relationship24" on "rule" ("track_id");
Create index "IX_Relationship33" on "service" ("ip_proto_id");
Create index "IX_Relationship36" on "service" ("svc_typ_id");
Create index "IX_Relationship37" on "object" ("zone_id");
Create index "IX_Relationship90" on "rule" ("rule_from_zone");
Create index "IX_Relationship91" on "rule" ("rule_to_zone");
Create index "IX_Relationship50" on "usergrp" ("usergrp_id");
Create index "IX_Relationship51" on "usergrp" ("usergrp_member_id");
Create index "IX_Relationship79" on "changelog_user" ("new_user_id");
Create index "IX_Relationship80" on "changelog_user" ("old_user_id");
Create index "IX_Relationship95" on "rule_from" ("user_id");

Create index "IX_Relationship59" on "import_service" ("control_id");
Create index "IX_Relationship60" on "import_object" ("control_id");
Create index "IX_Relationship61" on "import_rule" ("control_id");
Create index "IX_Relationship62" on "import_user" ("control_id");
Create index "IX_Relationship68" on "changelog_object" ("control_id");
Create index "IX_Relationship76" on "changelog_service" ("control_id");
Create index "IX_Relationship77" on "changelog_user" ("control_id");
Create index "IX_Relationship78" on "changelog_rule" ("control_id");
Create index "IX_Relationship185" on "import_changelog" ("control_id");

Create index "IX_Relationship107" on "objgrp_flat" ("import_created");
Create index "IX_Relationship108" on "objgrp_flat" ("import_last_seen");
Create index "IX_Relationship120" on "objgrp" ("import_created");
Create index "IX_Relationship121" on "objgrp" ("import_last_seen");
Create index "IX_Relationship122" on "svcgrp" ("import_created");
Create index "IX_Relationship123" on "svcgrp" ("import_last_seen");
Create index "IX_Relationship124" on "svcgrp_flat" ("import_created");
Create index "IX_Relationship125" on "svcgrp_flat" ("import_last_seen");
Create index "IX_Relationship132" on "import_zone" ("control_id");
Create index "IX_Relationship151" on "usergrp_flat" ("import_created");
Create index "IX_Relationship152" on "usergrp_flat" ("import_last_seen");
Create index "IX_Relationship153" on "usergrp" ("import_created");
Create index "IX_Relationship154" on "usergrp" ("import_last_seen");
Create index "IX_Relationship166" on "rule" ("rule_create");
Create index "IX_Relationship167" on "rule" ("rule_last_seen");
Create index "IX_Relationship168" on "rule_from" ("rf_create");
Create index "IX_Relationship169" on "rule_from" ("rf_last_seen");
Create index "IX_Relationship170" on "rule_to" ("rt_create");
Create index "IX_Relationship171" on "rule_to" ("rt_last_seen");
Create index "IX_Relationship172" on "rule_service" ("rs_create");
Create index "IX_Relationship173" on "rule_service" ("rs_last_seen");
Create index "IX_Relationship174" on "object" ("obj_create");
Create index "IX_Relationship175" on "object" ("obj_last_seen");
Create index "IX_Relationship176" on "service" ("svc_create");
Create index "IX_Relationship177" on "service" ("svc_last_seen");
