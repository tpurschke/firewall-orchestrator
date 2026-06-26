insert into config (config_key, config_value, config_user) VALUES ('importPathAnalysisDataStartAt', '00:00:00', 0) ON CONFLICT DO NOTHING;
insert into config (config_key, config_value, config_user) VALUES ('importPathAnalysisDataSleepTime', '0', 0) ON CONFLICT DO NOTHING;
insert into config (config_key, config_value, config_user) VALUES ('importPathAnalysisDataPath', '[]', 0) ON CONFLICT DO NOTHING;
insert into config (config_key, config_value, config_user) VALUES ('pathAnalysisMode', 'GatewayRoutingTable', 0) ON CONFLICT DO NOTHING;
