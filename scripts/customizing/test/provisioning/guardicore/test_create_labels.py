import argparse

import pytest

from scripts.customizing.provisioning.guardicore import create_guardicore_labels as sut


# Test parse_app_ids
def test_parse_app_ids_valid():
    input_str = '["APP-123", "APP-456"]'
    expected = ["APP-123", "APP-456"]
    assert sut.parse_app_ids(input_str) == expected


def test_parse_app_ids_invalid_json():
    with pytest.raises(argparse.ArgumentTypeError):
        sut.parse_app_ids("invalid-json")


def test_parse_app_ids_not_list():
    with pytest.raises(argparse.ArgumentTypeError):
        sut.parse_app_ids('{"key": "value"}')


def test_parse_app_ids_not_string_list():
    with pytest.raises(argparse.ArgumentTypeError):
        sut.parse_app_ids("[1, 2]")


# Test parse_group_types
def test_parse_group_types_valid():
    input_str = "[20, 21]"
    expected = [20, 21]
    assert sut.parse_group_types(input_str) == expected


def test_parse_group_types_invalid_json():
    with pytest.raises(argparse.ArgumentTypeError):
        sut.parse_group_types("invalid")


def test_parse_group_types_empty():
    with pytest.raises(argparse.ArgumentTypeError):
        sut.parse_group_types("[]")


# Test build_graphql_variables
def test_build_graphql_variables_defaults():
    vars_out = sut.build_graphql_variables()
    assert "ownerFilter" in vars_out
    or_clause = vars_out["ownerFilter"]["_or"]
    # Expect 1 OR clause: the AND condition for group types
    assert len(or_clause) == 1
    # Check default group types
    assert or_clause[0]["_and"][0]["nwgroups"]["group_type"]["_in"] == [20, 21]


def test_build_graphql_variables_with_apps():
    apps = ["APP-1"]
    vars_out = sut.build_graphql_variables(app_ids=apps)
    and_conditions = vars_out["ownerFilter"]["_or"][0]["_and"]
    # Check if app filter is present
    assert {"app_id_external": {"_in": apps}} in and_conditions


def test_build_graphql_variables_common_services():
    vars_out = sut.build_graphql_variables(include_common_services=True)
    or_clause = vars_out["ownerFilter"]["_or"]
    # Expect 2 OR clauses: one for group/app logic, one for common services
    assert len(or_clause) == 2
    assert {"common_service_possible": {"_eq": True}} in or_clause


# Test normalize_ip
def test_normalize_ip():
    assert sut.normalize_ip("192.168.1.1/32") == "192.168.1.1"
    assert sut.normalize_ip("10.0.0.1") == "10.0.0.1"


# Test criteria_from_network
def test_criteria_from_network_subnet():
    c = sut.criteria_from_network("1.1.1.1", "1.1.1.1")
    assert c is not None
    assert c.op == "SUBNET"
    assert c.argument == "1.1.1.1"


def test_criteria_from_network_range():
    c = sut.criteria_from_network("1.1.1.1", "1.1.1.5")
    assert c is not None
    assert c.op == "RANGE"
    assert c.argument == "1.1.1.1-1.1.1.5"


def test_criteria_from_network_none():
    assert sut.criteria_from_network(None, "1.1.1.1") is None


# Test label_key_from_id
def test_label_key_from_id():
    assert sut.label_key_from_id("AZ100") == sut.DEFAULT_GUARDICORE_KEY_APPZONE
    assert sut.label_key_from_id("AR200") == sut.DEFAULT_GUARDICORE_KEY_APPROLE
    assert sut.label_key_from_id("OTHER") is None


# Test chunked
def test_chunked():
    items = [1, 2, 3, 4, 5]
    chunks = list(sut.chunked(items, 2))  # pyright: ignore[reportArgumentType]
    assert len(chunks) == 3
    assert chunks[0] == [1, 2]
    assert chunks[2] == [5]


# Test build_labels_from_response (Logic test)
def test_build_labels_full_flow():
    mock_response = {
        "data": {
            "owner": [
                {
                    "name": "TestOwner",
                    "app_id_external": "APP-001",
                    "nwgroups": [
                        {
                            "name": "TestGroup",
                            "id_string": "AZ-GRP",
                            "nwobject_nwgroups": [{"owner_network": {"ip": "10.0.0.1", "ip_end": "10.0.0.1"}}],
                        }
                    ],
                }
            ]
        }
    }

    labels = sut.build_labels_from_response(mock_response)
    assert len(labels) == 1
    label = labels[0]
    assert label.key == sut.DEFAULT_GUARDICORE_KEY_APPZONE
    assert "TestOwner" in label.value
    assert len(label.criteria) == 1
    assert label.criteria[0].op == "SUBNET"
    assert label.criteria[0].argument == "10.0.0.1"


def test_require_login_fields():
    # Helper to create dummy args
    args = argparse.Namespace(fwo_jwt=None, fwo_user=None, fwo_password=None, fwo_middleware_url=None)
    with pytest.raises(sut.GuardicoreProvisioningError):
        sut.require_login_fields(args)

    args.fwo_jwt = "some-token"
    # Should pass
    sut.require_login_fields(args)

    args.fwo_jwt = None
    args.fwo_user = "user"
    args.fwo_middleware_url = "url"
    # Should pass
    sut.require_login_fields(args)
