-- generic (non-firewall) config items and their recertification (#5786)
-- keep in sync with roles/database/files/sql/creation/fworch-create-tables-generic.sql
-- and the generic parts of fworch-create-constraints.sql, fworch-create-foreign-keys.sql,
-- fworch-create-indices.sql and fworch-fill-stm.sql

CREATE SCHEMA IF NOT EXISTS generic;

CREATE TABLE IF NOT EXISTS generic.device_type_key_config
(
	id SERIAL PRIMARY KEY,
	dev_typ_id INTEGER NOT NULL,
	uid_key VARCHAR NOT NULL,
	name_key VARCHAR,
	order_key VARCHAR,
	owner_key VARCHAR,
	owner_match VARCHAR NOT NULL DEFAULT 'name',
	last_used_key VARCHAR,
	last_used_format VARCHAR,
	reportable_fields VARCHAR[] NOT NULL DEFAULT '{}'
);

CREATE TABLE IF NOT EXISTS generic.config_item
(
	id BIGSERIAL PRIMARY KEY,
	mgm_id INTEGER NOT NULL,
	item_uid VARCHAR NOT NULL,
	item_name VARCHAR,
	item_order INTEGER,
	config_line JSONB NOT NULL,
	owner_id INTEGER,
	last_used TIMESTAMP WITH TIME ZONE,
	first_seen TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT now(),
	created BIGINT NOT NULL,
	removed BIGINT
);

CREATE TABLE IF NOT EXISTS generic.recertification
(
	id BIGSERIAL PRIMARY KEY,
	config_item_id BIGINT NOT NULL,
	owner_id INTEGER,
	user_dn VARCHAR NOT NULL,
	recertified BOOLEAN NOT NULL DEFAULT false,
	recert_date TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT now(),
	comment VARCHAR,
	next_recert_date TIMESTAMP WITH TIME ZONE,
	owner_recert_id BIGINT
);

CREATE TABLE IF NOT EXISTS generic.owner_recertification
(
	id BIGSERIAL PRIMARY KEY,
	owner_id INTEGER NOT NULL,
	user_dn VARCHAR NOT NULL,
	recertified BOOLEAN NOT NULL DEFAULT false,
	recert_date TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT now(),
	comment VARCHAR,
	next_recert_date TIMESTAMP WITH TIME ZONE,
	report_id BIGINT
);

-- constraints and foreign keys, each guarded so the script can run repeatedly
DO $$
DECLARE
	constraintDef RECORD;
BEGIN
	FOR constraintDef IN
		SELECT * FROM (VALUES
			('generic.device_type_key_config'::regclass, 'generic_device_type_key_config_dev_typ_id_unique',
				'ALTER TABLE generic.device_type_key_config ADD CONSTRAINT generic_device_type_key_config_dev_typ_id_unique UNIQUE (dev_typ_id)'),
			('generic.device_type_key_config'::regclass, 'generic_device_type_key_config_owner_match_check',
				'ALTER TABLE generic.device_type_key_config ADD CONSTRAINT generic_device_type_key_config_owner_match_check CHECK ( owner_match IN (''name'', ''ext_id'') )'),
			('generic.device_type_key_config'::regclass, 'generic_device_type_key_config_last_used_format_check',
				'ALTER TABLE generic.device_type_key_config ADD CONSTRAINT generic_device_type_key_config_last_used_format_check CHECK ( last_used_format IS NULL OR last_used_format IN (''iso8601'', ''unix'', ''unix_ms'') )'),
			('generic.device_type_key_config'::regclass, 'generic_device_type_key_config_stm_dev_typ_foreign_key',
				'ALTER TABLE generic.device_type_key_config ADD CONSTRAINT generic_device_type_key_config_stm_dev_typ_foreign_key FOREIGN KEY (dev_typ_id) REFERENCES stm_dev_typ(dev_typ_id) ON UPDATE RESTRICT ON DELETE CASCADE'),
			('generic.config_item'::regclass, 'generic_config_item_management_foreign_key',
				'ALTER TABLE generic.config_item ADD CONSTRAINT generic_config_item_management_foreign_key FOREIGN KEY (mgm_id) REFERENCES management(mgm_id) ON UPDATE RESTRICT ON DELETE CASCADE'),
			('generic.config_item'::regclass, 'generic_config_item_owner_foreign_key',
				'ALTER TABLE generic.config_item ADD CONSTRAINT generic_config_item_owner_foreign_key FOREIGN KEY (owner_id) REFERENCES owner(id) ON UPDATE RESTRICT ON DELETE SET NULL'),
			('generic.config_item'::regclass, 'generic_config_item_created_import_control_foreign_key',
				'ALTER TABLE generic.config_item ADD CONSTRAINT generic_config_item_created_import_control_foreign_key FOREIGN KEY (created) REFERENCES import_control(control_id) ON UPDATE RESTRICT ON DELETE CASCADE'),
			('generic.config_item'::regclass, 'generic_config_item_removed_import_control_foreign_key',
				'ALTER TABLE generic.config_item ADD CONSTRAINT generic_config_item_removed_import_control_foreign_key FOREIGN KEY (removed) REFERENCES import_control(control_id) ON UPDATE RESTRICT ON DELETE CASCADE'),
			('generic.recertification'::regclass, 'generic_recertification_config_item_foreign_key',
				'ALTER TABLE generic.recertification ADD CONSTRAINT generic_recertification_config_item_foreign_key FOREIGN KEY (config_item_id) REFERENCES generic.config_item(id) ON UPDATE RESTRICT ON DELETE CASCADE'),
			('generic.recertification'::regclass, 'generic_recertification_owner_foreign_key',
				'ALTER TABLE generic.recertification ADD CONSTRAINT generic_recertification_owner_foreign_key FOREIGN KEY (owner_id) REFERENCES owner(id) ON UPDATE RESTRICT ON DELETE CASCADE'),
			('generic.recertification'::regclass, 'generic_recertification_owner_recertification_foreign_key',
				'ALTER TABLE generic.recertification ADD CONSTRAINT generic_recertification_owner_recertification_foreign_key FOREIGN KEY (owner_recert_id) REFERENCES generic.owner_recertification(id) ON UPDATE RESTRICT ON DELETE CASCADE'),
			('generic.owner_recertification'::regclass, 'generic_owner_recertification_owner_foreign_key',
				'ALTER TABLE generic.owner_recertification ADD CONSTRAINT generic_owner_recertification_owner_foreign_key FOREIGN KEY (owner_id) REFERENCES owner(id) ON UPDATE RESTRICT ON DELETE CASCADE'),
			('generic.owner_recertification'::regclass, 'generic_owner_recertification_report_foreign_key',
				'ALTER TABLE generic.owner_recertification ADD CONSTRAINT generic_owner_recertification_report_foreign_key FOREIGN KEY (report_id) REFERENCES report(report_id) ON UPDATE RESTRICT ON DELETE CASCADE')
		) AS c(tableId, constraintName, ddl)
	LOOP
		IF NOT EXISTS (
			SELECT 1 FROM pg_constraint
			WHERE conrelid = constraintDef.tableId AND conname = constraintDef.constraintName
		) THEN
			EXECUTE constraintDef.ddl;
		END IF;
	END LOOP;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS uidx_generic_config_item_active_uid ON generic.config_item (mgm_id, item_uid) WHERE removed IS NULL;
CREATE INDEX IF NOT EXISTS idx_generic_config_item_mgm_id_removed ON generic.config_item (mgm_id, removed);
CREATE INDEX IF NOT EXISTS idx_generic_config_item_owner_id ON generic.config_item (owner_id);
CREATE INDEX IF NOT EXISTS idx_generic_config_item_created ON generic.config_item (created);
CREATE INDEX IF NOT EXISTS idx_generic_config_item_removed ON generic.config_item (removed) WHERE removed IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_generic_recertification_owner_id ON generic.recertification (owner_id);
CREATE INDEX IF NOT EXISTS idx_generic_recertification_next_recert_date ON generic.recertification (next_recert_date);
CREATE INDEX IF NOT EXISTS idx_generic_recertification_config_item_id ON generic.recertification (config_item_id);
CREATE INDEX IF NOT EXISTS idx_generic_owner_recertification_owner_id ON generic.owner_recertification (owner_id);

-- Skyhigh Web Gateway as first generic device type
insert into stm_dev_typ (dev_typ_id,dev_typ_name,dev_typ_version,dev_typ_manufacturer,dev_typ_predef_svc,dev_typ_is_multi_mgmt,dev_typ_is_mgmt,is_pure_routing_device)
    VALUES (33,'Skyhigh Web Gateway','12ff','Skyhigh Security','',false,true,false) ON CONFLICT DO NOTHING;

insert into generic.device_type_key_config (dev_typ_id, uid_key, name_key, order_key, owner_key, owner_match, last_used_key, last_used_format, reportable_fields)
    VALUES (33, 'uid', 'name', NULL, 'owner', 'ext_id', NULL, NULL, '{path,name,enabled,condition,action,description}') ON CONFLICT (dev_typ_id) DO NOTHING;

-- make the generic report types (61 items, 62 recertification, 63 unused items) available
-- without touching any other customized entry of the list
UPDATE config
SET config_value = (
	SELECT jsonb_agg(reportType ORDER BY reportType)::text
	FROM (
		SELECT DISTINCT value::int AS reportType
		FROM jsonb_array_elements_text(config_value::jsonb)
		UNION
		SELECT unnest(ARRAY[61, 62, 63])
	) AS reportTypes
)
WHERE config_key = 'availableReportTypes' AND config_user = 0
	AND config_value IS NOT NULL AND config_value <> ''
	AND jsonb_typeof(config_value::jsonb) = 'array'
	AND NOT (config_value::jsonb @> '[61, 62, 63]'::jsonb);

-- read-only user (fresh installs get these grants through db_schemata)
GRANT USAGE ON SCHEMA generic TO fwo_ro;
GRANT SELECT ON ALL TABLES IN SCHEMA generic TO fwo_ro;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA generic TO fwo_ro;
ALTER DEFAULT PRIVILEGES IN SCHEMA generic GRANT SELECT ON TABLES TO fwo_ro;
ALTER DEFAULT PRIVILEGES IN SCHEMA generic GRANT USAGE, SELECT ON SEQUENCES TO fwo_ro;
