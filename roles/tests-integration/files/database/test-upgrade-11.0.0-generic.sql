-- Test of the generic schema of upgrade/11.0.0.sql (#5786). The test task inserts the upgrade file at the two
-- run-upgrade markers below, which also shows that the upgrade can be repeated on a fresh as well as on an upgraded
-- installation. This probes: the generic tables, constraints and indexes have the same names as in the creation
-- scripts, the Skyhigh device type and its key configuration exist once, the generic report types are available,
-- only one active version per item is possible, invalid key configurations are rejected, and a device type
-- can be assigned to a responsible type only once.
-- The transaction is always rolled back.
\set ON_ERROR_STOP 1
BEGIN;

-- @run-upgrade-11.0.0

-- @run-upgrade-11.0.0

DO $$
DECLARE
    missing text;
BEGIN
    SELECT string_agg(expected.name, ', ') INTO missing
    FROM (VALUES
        ('generic_device_type_key_config_dev_typ_id_unique'),
        ('generic_device_type_key_config_owner_match_check'),
        ('generic_device_type_key_config_last_used_format_check'),
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
        ('generic_responsible_type_dev_type_owner_responsible_type_foreign_key'),
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
    IF (SELECT count(*) FROM generic.device_type_key_config WHERE dev_typ_id = 33 AND uid_key = 'uid' AND owner_match = 'ext_id') <> 1 THEN
        RAISE EXCEPTION 'Skyhigh key configuration missing or duplicated';
    END IF;

    IF NOT ((SELECT config_value::jsonb FROM config WHERE config_key = 'availableReportTypes' AND config_user = 0) @> '[61, 62, 63]'::jsonb) THEN
        RAISE EXCEPTION 'generic report types are not available';
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
        INSERT INTO generic.device_type_key_config (dev_typ_id, uid_key, owner_match) VALUES (-1, 'uid', 'anything');
        RAISE EXCEPTION 'an unknown owner match was accepted';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;
    BEGIN
        INSERT INTO generic.device_type_key_config (dev_typ_id, uid_key, last_used_format) VALUES (-1, 'uid', 'dd.mm.yyyy');
        RAISE EXCEPTION 'an unknown last-used format was accepted';
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
        INSERT INTO generic.device_type_key_config (dev_typ_id, uid_key) VALUES (33, 'other');
        RAISE EXCEPTION 'a second key configuration for a device type was accepted';
    EXCEPTION WHEN unique_violation THEN
        NULL;
    END;
END $$;

ROLLBACK;
