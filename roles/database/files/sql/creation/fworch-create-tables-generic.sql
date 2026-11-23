--- Generic (non-firewall) config items and their recertification ---
-- Rule-like config items of devices that are not firewalls (proxy, load balancer, ...).
-- Keep in sync with the generic part of roles/database/files/upgrade/10.1.0.sql.
create schema generic;

-- per device type settings of generic config items; the items themselves have the same core fields for all
-- device types (uid, name, scope, enabled, match, action, description, owner, attributes, raw)
-- owner_match: the owner value of an item is an owner name ('name') or external app id ('ext_id')
-- has_last_used: the device type delivers the last use of its items (needed by the unused items report)
-- reportable_fields: keys of the item attributes shown as additional columns in reports
create table generic.device_type_key_config
(
	id SERIAL PRIMARY KEY,
	dev_typ_id INTEGER NOT NULL,
	owner_match VARCHAR NOT NULL DEFAULT 'name',
	has_last_used BOOLEAN NOT NULL DEFAULT false,
	reportable_fields VARCHAR[] NOT NULL DEFAULT '{}'
);

-- one rule-like config item per row, versioned by import like firewall.rule
-- item_order and last_used are updated in place, all other changes create a new version
create table generic.config_item
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

-- recertification decisions per item; append-only
-- config_item_id is the certified version, the item itself is identified by its (mgm_id, item_uid),
-- so a decision stays valid for later versions until the next due date
create table generic.recertification
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

-- owner-level recertification of all generic items of an owner
create table generic.owner_recertification
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

-- generic device types whose items users of an owner responsible type may recertify
-- a responsible type without rows may not recertify any generic items
create table generic.responsible_type_dev_type
(
	owner_responsible_type_id INTEGER NOT NULL,
	dev_typ_id INTEGER NOT NULL,
	PRIMARY KEY (owner_responsible_type_id, dev_typ_id)
);
