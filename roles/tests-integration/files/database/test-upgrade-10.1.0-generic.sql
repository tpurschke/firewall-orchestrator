-- Test of the generic schema of upgrade/10.1.0.sql (#5786). The test task inserts the upgrade file at each
-- run-upgrade marker below. The first two show that the upgrade can be repeated on a fresh as well as on an
-- upgraded installation. This probes: the generic tables, constraints and indexes have the same names as in the
-- creation scripts, the Skyhigh device type and its key configuration exist once, the generic report types are
-- available, an existing module list is left unchanged while a missing or empty one is pinned to the modules before
-- 10.1.0 (so the generic recertification module stays off), only one active version per item is possible, invalid
-- key configurations are rejected, and a device type can be assigned to a responsible type only once.
-- The transaction is always rolled back.
\set ON_ERROR_STOP 1
BEGIN;

-- @run-upgrade-10.1.0

-- @run-upgrade-10.1.0

DO $$
DECLARE
    missing text;
BEGIN
    SELECT string_agg(expected.name, ', ') INTO missing
    FROM (VALUES
        ('generic_device_type_key_config_dev_typ_id_unique'),
        ('generic_device_type_key_config_owner_match_check'),
        ('generic_device_type_key_config_stm_dev_typ_foreign_key'),
        ('generic_config_item_management_foreign_key'),
        ('generic_config_item_owner_foreign_key'),
        ('generic_config_item_created_import_control_foreign_key'),
        ('generic_config_item_removed_import_control_foreign_key'),
        ('generic_recertification_config_item_foreign_key'),
        ('generic_recertification_owner_foreign_key'),
        ('generic_recertification_owner_recertification_foreign_key'),
        ('generic_owner_recertification_owner_foreign_key'),
        ('generic_owner_recertification_report_foreign_key'),
        ('generic_responsible_type_dev_type_responsible_type_foreign_key'),
        ('generic_responsible_type_dev_type_stm_dev_typ_foreign_key')
    ) AS expected(name)
    WHERE NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = expected.name);
    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'generic constraints missing: %', missing;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'generic' AND indexname = 'uidx_generic_config_item_active_uid') THEN
        RAISE EXCEPTION 'unique index of active generic config items missing';
    END IF;

    IF (SELECT count(*) FROM stm_dev_typ WHERE dev_typ_id = 33 AND dev_typ_name = 'Skyhigh Web Gateway') <> 1 THEN
        RAISE EXCEPTION 'Skyhigh device type missing';
    END IF;
    -- Skyhigh rules have no last use and no attributes worth an extra column, the core columns show the rule
    IF (SELECT count(*) FROM generic.device_type_key_config
        WHERE dev_typ_id = 33 AND owner_match = 'ext_id' AND NOT has_last_used AND reportable_fields = '{}') <> 1 THEN
        RAISE EXCEPTION 'Skyhigh key configuration missing, duplicated or unexpected';
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'generic' AND table_name = 'device_type_key_config'
            AND column_name IN ('uid_key', 'name_key', 'order_key', 'owner_key', 'last_used_key', 'last_used_format')) THEN
        RAISE EXCEPTION 'key configuration still has key path columns';
    END IF;

    IF NOT ((SELECT config_value::jsonb FROM config WHERE config_key = 'availableReportTypes' AND config_user = 0) @> '[61, 62, 63]'::jsonb) THEN
        RAISE EXCEPTION 'generic report types are not available';
    END IF;

    -- this (fresh) installation offers the generic recertification module, the upgrade leaves the list as it is
    IF NOT ((SELECT config_value::jsonb FROM config WHERE config_key = 'availableModules' AND config_user = 0) @> '[7]'::jsonb) THEN
        RAISE EXCEPTION 'the upgrade changed an existing module list';
    END IF;
END $$;

-- an installation without module list (meaning "all modules") gets the modules existing before 10.1.0
DELETE FROM config WHERE config_key = 'availableModules' AND config_user = 0;

-- @run-upgrade-10.1.0

DO $$
BEGIN
    IF (SELECT config_value FROM config WHERE config_key = 'availableModules' AND config_user = 0) IS DISTINCT FROM '[1,2,3,4,5,6]' THEN
        RAISE EXCEPTION 'a missing module list was not pinned to the modules before 10.1.0';
    END IF;
END $$;

-- the same for an empty module list
UPDATE config SET config_value = '' WHERE config_key = 'availableModules' AND config_user = 0;

-- @run-upgrade-10.1.0

DO $$
BEGIN
    IF (SELECT config_value FROM config WHERE config_key = 'availableModules' AND config_user = 0) IS DISTINCT FROM '[1,2,3,4,5,6]' THEN
        RAISE EXCEPTION 'an empty module list was not pinned to the modules before 10.1.0';
    END IF;
END $$;

-- the foreign keys are switched off for the rows below, they only test the rules within the generic tables
SET LOCAL session_replication_role = replica;
INSERT INTO generic.config_item (id, mgm_id, item_uid, config_line, created) VALUES (-1, -1, 'probe', '{}', -1);
-- a removed version and a new active version of the same item may exist side by side
UPDATE generic.config_item SET removed = -2 WHERE id = -1;
INSERT INTO generic.config_item (id, mgm_id, item_uid, config_line, created) VALUES (-2, -1, 'probe', '{}', -2);

DO $$
BEGIN
    BEGIN
        INSERT INTO generic.config_item (id, mgm_id, item_uid, config_line, created) VALUES (-3, -1, 'probe', '{}', -3);
        RAISE EXCEPTION 'two active versions of one item were accepted';
    EXCEPTION WHEN unique_violation THEN
        NULL;
    END;
    BEGIN
        INSERT INTO generic.device_type_key_config (dev_typ_id, owner_match) VALUES (-1, 'anything');
        RAISE EXCEPTION 'an unknown owner match was accepted';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;
    INSERT INTO generic.responsible_type_dev_type (owner_responsible_type_id, dev_typ_id) VALUES (-1, 33);
    BEGIN
        INSERT INTO generic.responsible_type_dev_type (owner_responsible_type_id, dev_typ_id) VALUES (-1, 33);
        RAISE EXCEPTION 'a device type was assigned twice to one responsible type';
    EXCEPTION WHEN unique_violation THEN
        NULL;
    END;
    BEGIN
        INSERT INTO generic.device_type_key_config (dev_typ_id) VALUES (33);
        RAISE EXCEPTION 'a second key configuration for a device type was accepted';
    EXCEPTION WHEN unique_violation THEN
        NULL;
    END;
END $$;

ROLLBACK;
