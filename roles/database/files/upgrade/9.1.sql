-- generic gateway rulebase schema and proxy management additions

-- add proxy management device type
insert into stm_dev_typ (dev_typ_id,dev_typ_name,dev_typ_version,dev_typ_manufacturer,dev_typ_predef_svc,dev_typ_is_multi_mgmt,dev_typ_is_mgmt,is_pure_routing_device)
    VALUES (30,'Proxy Management','API','Proxy','',false,true,false) ON CONFLICT DO NOTHING;

DO $do$ BEGIN
  IF EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = 'proxy')
     AND NOT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = 'generic_gateway') THEN
    EXECUTE 'ALTER SCHEMA proxy RENAME TO generic_gateway';
  END IF;
END $do$;

CREATE SCHEMA IF NOT EXISTS generic_gateway;

DO $do$ BEGIN
  IF EXISTS (
    SELECT 1 FROM information_schema.tables
    WHERE table_schema = 'generic_gateway' AND table_name = 'proxy_rule'
  ) AND NOT EXISTS (
    SELECT 1 FROM information_schema.tables
    WHERE table_schema = 'generic_gateway' AND table_name = 'rulebase'
  ) THEN
    EXECUTE 'ALTER TABLE generic_gateway.proxy_rule RENAME TO rulebase';
  END IF;
END $do$;

CREATE TABLE IF NOT EXISTS generic_gateway.rulebase
(
    rule_id TEXT PRIMARY KEY,
    management_id INTEGER NOT NULL,
    management_name TEXT,
    owner_id INTEGER,
    owner_name TEXT,
    next_recert_date TIMESTAMPTZ,
    last_recertified TIMESTAMPTZ,
    last_recertifier TEXT,
    recertified BOOLEAN NOT NULL DEFAULT FALSE,
    comment TEXT,
    rule_json JSONB NOT NULL,
    imported_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

DO $do$ BEGIN
  IF EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'generic_gateway' AND indexname = 'idx_proxy_rule_mgmt')
     AND NOT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'generic_gateway' AND indexname = 'idx_rulebase_mgmt') THEN
    EXECUTE 'ALTER INDEX generic_gateway.idx_proxy_rule_mgmt RENAME TO idx_rulebase_mgmt';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'generic_gateway' AND indexname = 'idx_proxy_rule_owner')
     AND NOT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'generic_gateway' AND indexname = 'idx_rulebase_owner') THEN
    EXECUTE 'ALTER INDEX generic_gateway.idx_proxy_rule_owner RENAME TO idx_rulebase_owner';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'generic_gateway' AND indexname = 'idx_proxy_rule_next_recert')
     AND NOT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'generic_gateway' AND indexname = 'idx_rulebase_next_recert') THEN
    EXECUTE 'ALTER INDEX generic_gateway.idx_proxy_rule_next_recert RENAME TO idx_rulebase_next_recert';
  END IF;
END $do$;

CREATE INDEX IF NOT EXISTS idx_rulebase_mgmt ON generic_gateway.rulebase (management_id);
CREATE INDEX IF NOT EXISTS idx_rulebase_owner ON generic_gateway.rulebase (owner_id);
CREATE INDEX IF NOT EXISTS idx_rulebase_next_recert ON generic_gateway.rulebase (next_recert_date);

GRANT USAGE ON SCHEMA generic_gateway TO configimporters;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA generic_gateway TO configimporters;
GRANT USAGE ON SCHEMA generic_gateway TO fworchadmins;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA generic_gateway TO fworchadmins;

DO $do$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM management WHERE mgm_name = 'skyhigh-proxy1') THEN
    INSERT INTO management (dev_typ_id, mgm_name, import_credential_id, ssh_hostname, do_not_import, importer_hostname)
    VALUES (30, 'skyhigh-proxy1', 0, '', false, 'localhost');
  END IF;
END $do$;
