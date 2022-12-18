create table if not exists recertification
(
	id BIGSERIAL PRIMARY KEY,
    rule_metadata_id bigint NOT NULL,
    rule_id bigint NOT NULL,
    ip_match varchar,
    owner_id int,
    user_dn varchar,
    recertified boolean default false,
	recert_date Timestamp,
	comment varchar
);

ALTER TABLE recertification DROP CONSTRAINT IF EXISTS recertification_rule_metadata_foreign_key;
ALTER TABLE recertification DROP CONSTRAINT IF EXISTS recertification_owner_foreign_key;
ALTER TABLE recertification ADD CONSTRAINT recertification_rule_metadata_foreign_key FOREIGN KEY (rule_metadata_id) REFERENCES rule_metadata(rule_metadata_id) ON UPDATE RESTRICT ON DELETE CASCADE;
ALTER TABLE recertification ADD CONSTRAINT recertification_owner_foreign_key FOREIGN KEY (owner_id) REFERENCES owner(id) ON UPDATE RESTRICT ON DELETE CASCADE;

insert into config (config_key, config_value, config_user) VALUES ('reqRulDelStateMatrix', '{"config_value":{"request":{"matrix":{"0":[0,49,620],"49":[49,620],"620":[620]},"derived_states":{"0":0,"49":49,"620":620},"lowest_input_state":0,"lowest_start_state":49,"lowest_end_state":49,"active":true},"approval":{"matrix":{"49":[60],"60":[60,99,610],"99":[99],"610":[610]},"derived_states":{"49":49,"60":60,"99":99,"610":610},"lowest_input_state":49,"lowest_start_state":60,"lowest_end_state":99,"active":true},"planning":{"matrix":{"99":[110],"110":[110,120,130,149],"120":[120,110,130,149],"130":[130,110,120,149,610],"149":[149],"610":[610]},"derived_states":{"99":99,"110":110,"120":110,"130":110,"149":149,"610":610},"lowest_input_state":99,"lowest_start_state":110,"lowest_end_state":149,"active":false},"verification":{"matrix":{"149":[160],"160":[160,199,610],"199":[199],"610":[610]},"derived_states":{"149":149,"160":160,"199":199,"610":610},"lowest_input_state":149,"lowest_start_state":160,"lowest_end_state":199,"active":false},"implementation":{"matrix":{"99":[210],"210":[210,220,249],"220":[220,210,249,610],"249":[249],"610":[610]},"derived_states":{"99":99,"210":210,"220":210,"249":249,"610":610},"lowest_input_state":99,"lowest_start_state":210,"lowest_end_state":249,"active":true},"review":{"matrix":{"249":[260],"260":[260,270,299],"270":[210,270,260,299,610],"299":[299],"610":[610]},"derived_states":{"249":249,"260":260,"270":260,"299":299,"610":610},"lowest_input_state":249,"lowest_start_state":260,"lowest_end_state":299,"active":false},"recertification":{"matrix":{"299":[310],"310":[310,349,400],"349":[349],"400":[400]},"derived_states":{"299":299,"310":310,"349":349,"400":400},"lowest_input_state":299,"lowest_start_state":310,"lowest_end_state":349,"active":false}}}', 0) ON CONFLICT DO NOTHING;
insert into config (config_key, config_value, config_user) VALUES ('reqRulModStateMatrix', '{"config_value":{"request":{"matrix":{"0":[0,49,620],"49":[49,620],"620":[620]},"derived_states":{"0":0,"49":49,"620":620},"lowest_input_state":0,"lowest_start_state":49,"lowest_end_state":49,"active":true},"approval":{"matrix":{"49":[60],"60":[60,99,610],"99":[99],"610":[610]},"derived_states":{"49":49,"60":60,"99":99,"610":610},"lowest_input_state":49,"lowest_start_state":60,"lowest_end_state":99,"active":true},"planning":{"matrix":{"99":[110],"110":[110,120,130,149],"120":[120,110,130,149],"130":[130,110,120,149,610],"149":[149],"610":[610]},"derived_states":{"99":99,"110":110,"120":110,"130":110,"149":149,"610":610},"lowest_input_state":99,"lowest_start_state":110,"lowest_end_state":149,"active":false},"verification":{"matrix":{"149":[160],"160":[160,199,610],"199":[199],"610":[610]},"derived_states":{"149":149,"160":160,"199":199,"610":610},"lowest_input_state":149,"lowest_start_state":160,"lowest_end_state":199,"active":false},"implementation":{"matrix":{"99":[210],"210":[210,220,249],"220":[220,210,249,610],"249":[249],"610":[610]},"derived_states":{"99":99,"210":210,"220":210,"249":249,"610":610},"lowest_input_state":99,"lowest_start_state":210,"lowest_end_state":249,"active":true},"review":{"matrix":{"249":[260],"260":[260,270,299],"270":[210,270,260,299,610],"299":[299],"610":[610]},"derived_states":{"249":249,"260":260,"270":260,"299":299,"610":610},"lowest_input_state":249,"lowest_start_state":260,"lowest_end_state":299,"active":false},"recertification":{"matrix":{"299":[310],"310":[310,349,400],"349":[349],"400":[400]},"derived_states":{"299":299,"310":310,"349":349,"400":400},"lowest_input_state":299,"lowest_start_state":310,"lowest_end_state":349,"active":false}}}', 0) ON CONFLICT DO NOTHING;
insert into config (config_key, config_value, config_user) VALUES ('reqRulDelStateMatrixDefault', '{"config_value":{"request":{"matrix":{"0":[0,49,620],"49":[49,620],"620":[620]},"derived_states":{"0":0,"49":49,"620":620},"lowest_input_state":0,"lowest_start_state":49,"lowest_end_state":49,"active":true},"approval":{"matrix":{"49":[60],"60":[60,99,610],"99":[99],"610":[610]},"derived_states":{"49":49,"60":60,"99":99,"610":610},"lowest_input_state":49,"lowest_start_state":60,"lowest_end_state":99,"active":true},"planning":{"matrix":{"99":[110],"110":[110,120,130,149],"120":[120,110,130,149],"130":[130,110,120,149,610],"149":[149],"610":[610]},"derived_states":{"99":99,"110":110,"120":110,"130":110,"149":149,"610":610},"lowest_input_state":99,"lowest_start_state":110,"lowest_end_state":149,"active":false},"verification":{"matrix":{"149":[160],"160":[160,199,610],"199":[199],"610":[610]},"derived_states":{"149":149,"160":160,"199":199,"610":610},"lowest_input_state":149,"lowest_start_state":160,"lowest_end_state":199,"active":false},"implementation":{"matrix":{"99":[210],"210":[210,220,249],"220":[220,210,249,610],"249":[249],"610":[610]},"derived_states":{"99":99,"210":210,"220":210,"249":249,"610":610},"lowest_input_state":99,"lowest_start_state":210,"lowest_end_state":249,"active":true},"review":{"matrix":{"249":[260],"260":[260,270,299],"270":[210,270,260,299,610],"299":[299],"610":[610]},"derived_states":{"249":249,"260":260,"270":260,"299":299,"610":610},"lowest_input_state":249,"lowest_start_state":260,"lowest_end_state":299,"active":false},"recertification":{"matrix":{"299":[310],"310":[310,349,400],"349":[349],"400":[400]},"derived_states":{"299":299,"310":310,"349":349,"400":400},"lowest_input_state":299,"lowest_start_state":310,"lowest_end_state":349,"active":false}}}', 0) ON CONFLICT DO NOTHING;
insert into config (config_key, config_value, config_user) VALUES ('reqRulModStateMatrixDefault', '{"config_value":{"request":{"matrix":{"0":[0,49,620],"49":[49,620],"620":[620]},"derived_states":{"0":0,"49":49,"620":620},"lowest_input_state":0,"lowest_start_state":49,"lowest_end_state":49,"active":true},"approval":{"matrix":{"49":[60],"60":[60,99,610],"99":[99],"610":[610]},"derived_states":{"49":49,"60":60,"99":99,"610":610},"lowest_input_state":49,"lowest_start_state":60,"lowest_end_state":99,"active":true},"planning":{"matrix":{"99":[110],"110":[110,120,130,149],"120":[120,110,130,149],"130":[130,110,120,149,610],"149":[149],"610":[610]},"derived_states":{"99":99,"110":110,"120":110,"130":110,"149":149,"610":610},"lowest_input_state":99,"lowest_start_state":110,"lowest_end_state":149,"active":false},"verification":{"matrix":{"149":[160],"160":[160,199,610],"199":[199],"610":[610]},"derived_states":{"149":149,"160":160,"199":199,"610":610},"lowest_input_state":149,"lowest_start_state":160,"lowest_end_state":199,"active":false},"implementation":{"matrix":{"99":[210],"210":[210,220,249],"220":[220,210,249,610],"249":[249],"610":[610]},"derived_states":{"99":99,"210":210,"220":210,"249":249,"610":610},"lowest_input_state":99,"lowest_start_state":210,"lowest_end_state":249,"active":true},"review":{"matrix":{"249":[260],"260":[260,270,299],"270":[210,270,260,299,610],"299":[299],"610":[610]},"derived_states":{"249":249,"260":260,"270":260,"299":299,"610":610},"lowest_input_state":249,"lowest_start_state":260,"lowest_end_state":299,"active":false},"recertification":{"matrix":{"299":[310],"310":[310,349,400],"349":[349],"400":[400]},"derived_states":{"299":299,"310":310,"349":349,"400":400},"lowest_input_state":299,"lowest_start_state":310,"lowest_end_state":349,"active":false}}}', 0) ON CONFLICT DO NOTHING;

