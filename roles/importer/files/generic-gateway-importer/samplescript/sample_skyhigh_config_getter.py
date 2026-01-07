# ruff: noqa: PLW0602, PLW0603
import configparser
import json
import logging
import os
import platform
import pprint
import sys
from logging.handlers import RotatingFileHandler
from typing import Any, Mapping, cast

import requests
import urllib3
import xmltodict  # type: ignore[import]

# common functions for logging and ini file
version = "2025-01-20a"
path_separator = "/"
base_path_windows = "C:\\SWGDocu\\"
base_path_linux = "/home/swgdocu/"
base_path = None
log_prefix = "SWGDocuLog"
log_extension = ".log"
debug_level = 2
logger = logging.getLogger("SWGDocu")
platform_name = None

OutList = list[list[str]]


def require_base_path() -> str:
    if base_path is None:
        raise RuntimeError("basePath is not initialized")
    return base_path


# restUrl = "http://192.168.108.52:4711/Konfigurator/REST/"
rest_url = "http://192.168.108.175:4711/Konfigurator/REST/"
auth_user = "admin"
auth_pw = "McAfee123!"

nesting_level = 0  # use as global variable to get a "by ref" parameter


# use returncode 0 to 255 = -1
def exit_all(return_code: int) -> None:
    append_log_dbg("sys.exit(" + str(return_code) + ")")
    os._exit(return_code)
    sys.exit(return_code)  # has problems


def prepare_log_and_more() -> None:
    global version
    global platform_name
    global path_separator
    global base_path_windows
    global base_path_linux
    global base_path
    global log_prefix
    global log_extension
    global debug_level
    global logger

    plf = platform.system()
    platform_name = str(plf)
    print("prepareLogAndMore: running on " + platform_name)
    if plf == "Windows":
        base_path = base_path_windows
        path_separator = "\\"
    else:
        base_path = base_path_linux
        path_separator = "/"
    base_path = require_base_path()
    if not os.path.exists(base_path):
        os.makedirs(base_path)
    log_filename = os.path.join(base_path, log_prefix + log_extension)

    # show all existing handlers and may be remove them
    for handler in logging.root.handlers[:]:
        print("handler in logging.root.handlers: ", handler)
        ###logging.root.removeHandler(handler)

    logging.basicConfig()
    logging.root.setLevel(logging.WARNING)  # will be overwritten with debugLevel later

    logger = logging.getLogger("SWGDocu")
    logger.setLevel(logging.DEBUG)

    handler_log = RotatingFileHandler(filename=log_filename, mode="w", maxBytes=200000000, backupCount=5)
    # datefmt="%Y/%m/%d_%I:%M:%S %p"
    formatter = logging.Formatter(
        "%(asctime)s <%(levelname)s> %(name)s[%(process)d %(threadName)s] : %(message)s", datefmt="%Y/%m/%d %H:%M:%S"
    )
    handler_log.setFormatter(formatter)
    logger.addHandler(handler_log)

    # create the start logging
    logger.info("....:....1....:....2....:....3....:....4....:....5....:....6....:....7....:....8")
    logger.info("Logging started with version " + version + " for " + plf)

    # if needed show the loggers to be modified
    for name in logging.root.manager.loggerDict:
        append_log_dbg("name in logging.root.manager.loggerDict: " + name)


def append_log_inf(line: str) -> None:
    logger.info(line)


def append_log_dbg(line: str) -> None:
    logger.debug(line)


def append_log_err(line: str) -> None:
    logger.error(line)

    # ini File Handling


def read_swg_docu_ini_file() -> bool:
    global debug_level
    global rest_url
    global auth_user
    global auth_pw

    try:
        docu_config = configparser.ConfigParser(interpolation=None)
        base_path = require_base_path()
        docu_config.read(os.path.join(base_path, "SWGDocu.ini"))
        for section in docu_config.sections():
            for item in docu_config[section]:
                val_bytes = str.encode(docu_config[section][item])
                append_log_dbg("Configuration: [" + section + "] " + item + ' = "' + str(val_bytes) + '"')

        debug_level = int(docu_config["global"]["debugLevel"])
        # turn on more logging
        if debug_level > 0:
            logging.root.setLevel(logging.INFO)
        if debug_level > 1:
            logging.root.setLevel(logging.DEBUG)

        rest_url = docu_config["swg"]["restUrl"]
        auth_user = docu_config["swg"]["authUser"]
        auth_pw = docu_config["swg"]["authPw"]

    except Exception as e:
        append_log_dbg("Exception at readSWGDocuIniFile: " + str(e))
        exit_all(1)

    return True


def dict_to_json(data: Any) -> str:
    return json.dumps(data)


def json_to_dict(json_string: str) -> Any:
    return json.loads(json_string.rstrip("\0"))


def clean_string(text: str) -> str:
    return (
        text.replace("ä", "ae")
        .replace("Ä", "Ae")
        .replace("ö", "oe")
        .replace("Ö", "Oe")
        .replace("ß", "ss")
        .replace("ü", "ue")
        .replace("Ü", "Ue")
        .replace(",", " ")
        .replace(".", " ")
        .replace("!", " ")
        .replace("?", " ")
        .replace(":", " ")
        .replace(";", " ")
        .replace("-", " ")
        .replace("_", " ")
        .replace(")", " ")
        .replace("(", " ")
        .replace("\n", " ")
        .replace(" ", " ")
    )

    # -------------------- SWG Docu Functions --------------------


def format_condition_expression(ce: Mapping[str, Any]) -> str:
    ce_fmt = ""
    # ceStr = str(ce)

    closing_bracket_count = int(ce["@closingBracketCount"])
    opening_bracket_count = int(ce["@openingBracketCount"])
    operator_id = ce["@operatorId"]
    prefix = ""
    if "@prefix" in ce:
        prefix = ce["@prefix"]
    property_instance = ce["propertyInstance"]
    property_entry = ""
    property_entry_value = ""
    if "parameters" in property_instance:
        property_parameters = property_instance["parameters"]
        if "entry" in property_parameters:
            if isinstance(property_parameters["entry"], list):
                property_entry = property_parameters["entry"]
                for pe in property_entry:
                    property_entry_value += str(pe["parameter"]["value"]) + "|"
                    property_entry = property_entry_value  # overwrite with a more specific value
            else:
                property_entry = str(property_parameters["entry"]["parameter"]["value"])
                if "stringValue" in property_parameters["entry"]["parameter"]["value"]:
                    property_entry_value += str(property_parameters["entry"]["parameter"]["value"]["stringValue"])
                    property_entry = property_entry_value  # overwrite with a more specific value
                if "value" in property_parameters["entry"]["parameter"]["value"]:
                    property_entry_value += str(property_parameters["entry"]["parameter"]["value"]["value"])
                    property_entry = property_entry_value  # overwrite with a more specific value
                if "listValue" in property_parameters["entry"]["parameter"]["value"]:
                    property_entry_value += str(property_parameters["entry"]["parameter"]["value"]["listValue"])
                    property_entry = property_entry_value  # overwrite with a more specific value
    parameter = ce["parameter"]

    while opening_bracket_count > 0:
        ce_fmt += "("
        opening_bracket_count -= 1

    operator_id = operator_id.replace("com.scur.operator.", "")
    property_id = str(property_instance["@propertyId"]).replace("com.scur.engine.system.", "")
    if property_id.isnumeric():
        property_id = "UserDefined." + property_id
    value_typ = parameter["@valueTyp"]
    type_id = parameter["@typeId"]
    value = ""
    if type_id == "com.scur.type.string" and value_typ == "3":
        value = '"' + parameter["value"]["stringValue"]["@value"] + '"'
    elif type_id == "com.scur.type.string" and value_typ == "2":
        value = str(parameter["value"])
    elif type_id == "com.scur.type.number" and value_typ == "3":
        value = parameter["value"]["stringValue"]["@value"]
    elif type_id == "com.scur.type.number" and value_typ == "2":
        value = str(parameter["value"])
    elif type_id == "com.scur.type.list":
        value = str(parameter["value"])
    elif type_id == "com.scur.type.boolean":
        value = str(parameter["@valueId"]).replace("com.scur.type.boolean.", "")
    elif type_id == "com.scur.type.regex":
        value = str(parameter["value"])
    elif type_id == "com.scur.type.category" and value_typ == "3":
        value = str(parameter["@valueId"]).replace("com.scur.", "")

    if value == "":
        value = parameter["value"]["stringValue"]["@value"]  # check for exeption
    if prefix == "":
        ce_fmt += property_id + property_entry + " " + operator_id + " " + value
    else:
        ce_fmt += prefix + " " + property_id + property_entry + " " + operator_id + " " + value

    while closing_bracket_count > 0:
        ce_fmt += ")"
        closing_bracket_count -= 1

        # remove some text
    ce_fmt = ce_fmt.replace("com.scur.engine.", "")
    ce_fmt = ce_fmt.replace("OrderedDict", "")
    ce_fmt = ce_fmt.replace("com.scur.", "")
    ce_fmt = ce_fmt.replace(", ('@stringModifier', 'false')", "")
    ce_fmt = ce_fmt.replace("headerfilter.", "")
    ce_fmt = ce_fmt.replace(", ('@stringModifier', 'true'), ('@typeId', 'type.string')", "")
    ce_fmt = ce_fmt.replace("'@id', ", "")
    ce_fmt = ce_fmt.replace("'@value', ", "")
    ce_fmt = ce_fmt.replace("'@typeId', ", "")
    ce_fmt = ce_fmt.replace("'listValue', ", "")
    ce_fmt = ce_fmt.replace("'stringValue', ", "")
    ce_fmt = ce_fmt.replace("certificatefilter.ssl.", "")
    ce_fmt = ce_fmt.replace("certificatechainfilter.ssl.", "")
    ce_fmt = ce_fmt.replace("('@useMostRecentConfiguration', 'false'), ", "")
    ce_fmt = ce_fmt.replace("", "")

    return ce_fmt


def format_expression(expr: Mapping[str, Any]) -> str:
    formatted_ce = ""

    if isinstance(expr["conditionExpression"], list):
        for ce in cast(list[Mapping[str, Any]], expr["conditionExpression"]):
            formatted_ce += format_condition_expression(ce) + " "
    else:
        ce = cast(Mapping[str, Any], expr["conditionExpression"])
        formatted_ce += format_condition_expression(ce)

    return formatted_ce.strip()


def format_condition(cond: Mapping[str, Any]) -> str:
    formatted_cond = ""

    always = str(cond["@always"])
    if always == "true":
        return "always"

    if isinstance(cond["expressions"], list):
        for expr in cast(list[Mapping[str, Any]], cond["expressions"]):
            formatted_cond += format_expression(expr)
    else:
        expr = cast(Mapping[str, Any], cond["expressions"])
        formatted_cond += format_expression(expr)

    return formatted_cond


def get_rule(rule: Mapping[str, Any], outlist: OutList) -> None:  # document a single rule
    name = rule["@name"]
    # PostIdent TEST
    enabled = rule["@enabled"]
    if enabled != "true":
        name += " (disabled)"

    description = rule["description"]
    if description is None:
        description = " "
    description = description.replace("\n", ";")
    description = description.replace("\t", " ")

    cond = format_condition(rule["condition"])

    action_id = str(rule["actionContainer"]["@actionId"])
    action_id = action_id.replace("com.scur.mainaction.", "")

    append_log_dbg('    rule = "' + name + '" descr = "' + description + '" cond = "' + cond + '"')
    # outlist = ["Path", "Condition", "Source", "Destination", "Action", "Comment"]
    outlist.append([" ", "    rule: " + name, cond, " ", " ", action_id, description])


def get_rules(rules: Mapping[str, Any], outlist: OutList) -> None:
    if isinstance(rules["rule"], list):
        for rule in cast(list[Mapping[str, Any]], rules["rule"]):
            get_rule(rule, outlist)
    else:
        rule = cast(Mapping[str, Any], rules["rule"])
        get_rule(rule, outlist)


def recurse_rule_groups(rule_group: Mapping[str, Any] | None, nesting_level: int, outlist: OutList) -> None:
    if rule_group is None:
        return

        # get the needed values
    name = rule_group["@name"]
    # if name == "1.40.01 Coaching":      # for a break at a ruleset
    #   b = 1
    enabled = rule_group["@enabled"]
    if enabled != "true":
        name += " (disabled)"

    description = rule_group["description"]
    if description is None:
        description = " "
    description = description.replace("\n", ";")
    description = description.replace("\t", " ")

    cond = format_condition(rule_group["condition"])

    append_log_dbg(
        "nestingLevel: " + str(nesting_level) + ' "' + name + '" descr = "' + description + '" cond = "' + cond + '"'
    )
    # outlist = ["Path", "Condition", "Source", "Destination", "Action", "Comment"]
    outlist.append([str(nesting_level), name, cond, " ", " ", " ", description])

    # document the rules
    rules = rule_group["rules"]
    if rules is not None:
        get_rules(rules, outlist)

        # recurse to the next ruleGroups
    next_rule_groups = rule_group["ruleGroups"]
    if next_rule_groups is None:
        return
    if isinstance(next_rule_groups["ruleGroup"], list):
        for group in next_rule_groups["ruleGroup"]:
            recurse_rule_groups(group, nesting_level + 1, outlist)
    else:
        group = next_rule_groups["ruleGroup"]
        recurse_rule_groups(group, nesting_level + 1, outlist)


def get_rule_set_href(cookies: Any, title: str, href: str, outlist: OutList) -> None:
    # global nestingLevel

    nesting_level = 0
    # get the ruleset as xml with REST-Api
    href += "/export"
    append_log_dbg(f"getRuleSetHref: {title} href = {href}")

    if "https" in rest_url.lower():
        rule_set = requests.post(href, cookies=cookies, verify=False)
    else:
        rule_set = requests.post(href, cookies=cookies)
    if rule_set.status_code != 200:
        append_log_dbg("getRuleSetHref: error " + str(rule_set.status_code) + " " + str(rule_set.content))
        return

    rule_set_dict = xmltodict.parse(rule_set.content)

    # now extract only some values
    library_content = rule_set_dict["libraryContent"]
    rule_group = library_content["ruleGroup"]
    # go through all nested rulegroups
    recurse_rule_groups(rule_group, nesting_level, outlist)


def document_rule_sets_and_rules(cookies: Any) -> OutList | None:
    global base_path
    global nesting_level

    append_log_dbg("documentRuleSetsAndRules:")
    if "https" in rest_url.lower():
        rule_sets = requests.get(rest_url + "rulesets?topLevelOnly=true", cookies=cookies, verify=False)
    else:
        rule_sets = requests.get(rest_url + "rulesets?topLevelOnly=true", cookies=cookies)
    if rule_sets.status_code != 200:
        append_log_dbg("documentRuleSetsAndRules: error " + str(rule_sets.status_code) + " " + str(rule_sets.content))
        return None

    rule_sets_dict = xmltodict.parse(rule_sets.content)
    # rs = pprint.pformat(ruleSetsDict, 2, 256)
    # appendLogDbg("getRuleSetsAndRules: topLevelRuleSets\n" + rs)

    lvl2 = rule_sets_dict["feed"]["entry"]
    append_log_dbg("documentRuleSetsAndRules:" + str(len(lvl2)) + " topLevelRuleSets returned")

    # this list will be filled
    outlist: OutList = []
    outlist.append(["Level", "Path", "Condition", "Source", "Destination", "Action", "Comment"])
    rule_sets_cnt = 0
    nesting_level = 0
    for entry in lvl2:
        rule_sets_cnt += 1
        append_log_dbg(
            "documentRuleSetsAndRules: ========== next topLevelRuleSet number " + str(rule_sets_cnt) + " =========="
        )
        rs = pprint.pformat(entry, 2, 256)
        append_log_dbg("    " + rs)

        link = entry["link"]
        # change title if doesn"t have the parent link
        new_title = entry["title"]
        enabled = entry["enabled"]
        if enabled != "true":
            new_title += " (disabled)"
        if "@href" in link:
            href = entry["link"]["@href"]
            title = new_title
            get_rule_set_href(cookies, title, href, outlist)

    append_log_dbg("documentRuleSetsAndRules: " + str(rule_sets_cnt) + " rule set entries extracted")

    # write the outlist to a file.Take care of Umlaute in the comment
    # ["Level", "Path", "Condition", "Source", "Destination", "Action", "Comment"])
    # with open(basePath + "RulesbyNames.csv", "w", encoding="utf-8") as outp1:      # ruins Umlaute
    #    for list in outlist:
    #        outp1.write(list[0] + "\t" + list[1] + "\t" + list[2] + "\t" + list[3] + "\t" + list[4] + "\t" + list[5] + "\t" + list[6].decode("iso-8859-1") + "\t" + "\n")

    base_path = require_base_path()
    with open(os.path.join(base_path, "RulesbyNames.csv"), "wb") as outp1:
        for row in outlist:
            outp1.write(bytes(row[0].encode("iso-8859-1")))
            outp1.write(b"\t")
            outp1.write(bytes(row[1].encode("iso-8859-1")))
            outp1.write(b"\t")
            outp1.write(bytes(row[2].encode("iso-8859-1")))
            outp1.write(b"\t")
            outp1.write(bytes(row[3].encode("iso-8859-1")))
            outp1.write(b"\t")
            outp1.write(bytes(row[4].encode("iso-8859-1")))
            outp1.write(b"\t")
            outp1.write(bytes(row[5].encode("iso-8859-1")))
            outp1.write(b"\t")
            outp1.write(bytes(row[6].encode("cp1252")))
            outp1.write(b"\n")

    append_log_dbg("documentRuleSetsAndRules: " + str(len(outlist)) + " list entries written to RulesbyNames.csv")
    outp1.close()

    return outlist


def document_lists(cookies: Any) -> list[Any] | None:
    if "https" in rest_url.lower():
        lists = requests.get(rest_url + "list", cookies=cookies, verify=False)
    else:
        lists = requests.get(rest_url + "list", cookies=cookies)
    if lists.status_code != 200:
        append_log_dbg("documentLists: error " + str(lists.status_code) + " " + str(lists.content))
        return None

    lists_dict = xmltodict.parse(lists.content)
    lvl2 = lists_dict["feed"]["entry"]
    print(str(len(lvl2)) + " lists returned")

    cnt = 1
    outlist: OutList = []
    list_cnt = 0
    for entry in lvl2:
        list_cnt += 1
        # if cnt > 20000:
        #    break
        # pprint.pprint(entry)
        list_link = entry["link"]["@href"]
        list_xml = requests.get(list_link, cookies=cookies)

        list_dict = xmltodict.parse(list_xml.content)
        title = list_dict["entry"]["title"]
        list_type = list_dict["entry"]["listType"]
        content = list_dict["entry"]["content"]["list"]["content"]

        if list_cnt % 1000 == 0:
            print("at list #" + str(list_cnt) + " " + title)

        if content is None:
            # print("entry (" + str(cnt) + "): " + listType + ", " + title + ", " + "None" + ", " + "None")
            outlist.append([str(cnt), str(list_type), str(title), "None", "None"])
            cnt += 1
            continue
        for list_entry in content["listEntry"]:
            if not isinstance(list_entry, str):
                if "description" in list_entry:
                    descr = list_entry["description"]
                    if descr is None:
                        descr = ""
                else:
                    descr = ""
                if "entry" in list_entry:
                    entry = list_entry["entry"]
                else:
                    entry = ""
                    # print("entry (" + str(cnt) + "): " + listType + ", " + title + ", " + entry + ", " + descr)
                outlist.append([str(cnt), str(list_type), str(title), str(entry), str(descr)])
                cnt += 1
            else:
                # print("entry (" + str(cnt) + "): " + listType + ", " + title + ", " + listEntry + ", " + "None")
                outlist.append([str(cnt), str(list_type), str(title), str(list_entry), "None"])
                cnt += 1

    print(str(cnt) + " list entries extracted")

    # print("entry (" + str(cnt) + "): " + listType + ", " + title + ", " + entry + ", " + descr)
    #                        0               1                 2             3              4
    sortedoutlist: OutList = sorted(outlist, key=lambda row: (row[1], row[3]))
    base_path = require_base_path()
    with open(os.path.join(base_path, "ListsbyTypeAndValue.csv"), "w", encoding="utf-8") as outp1:
        outp1.write(
            "entry number\t" + "list type" + "\t" + "entry" + "\t" + "entry description" + "\t" + "list title" + "\n"
        )
        for row in sortedoutlist:
            nbr = str(row[0])
            list_type = row[1]
            title = row[2].replace("\n", " ")
            title = title.replace("\t", " ")
            entry = row[3].replace("\t", " ")
            descr = row[4].replace("\n", " ")
            descr = descr.replace("\t", " ")
            ###outp1.write("("+ nbr + ")\t" + listType + "\t" + """ + entry + "\t" + descr + "\t" + title + "\n")
            outp1.write("(" + nbr + ")\t" + list_type + "\t" + entry + "\t" + descr + "\t" + title + "\n")

    print(str(cnt) + " list entries written to ListsbyTypeAndValue.csv")

    sortedoutlist: OutList = sorted(outlist, key=lambda row: (row[1], row[2], row[3]))
    base_path = require_base_path()
    with open(os.path.join(base_path, "ListsbyTypeAndTitle.csv"), "w", encoding="utf-8") as outp2:
        outp2.write(
            "entry number\t" + "list type" + "\t" + "list title" + "\t" + "entry" + "\t" + "entry description" + "\n"
        )
        for row in sortedoutlist:
            nbr = str(row[0])
            list_type = row[1]
            title = row[2].replace("\n", " ")
            title = title.replace("\t", " ")
            entry = row[3].replace("\t", " ")
            descr = row[4].replace("\n", " ")
            descr = descr.replace("\t", " ")
            ###outp2.write("("+ nbr + ")\t" + listType + "\t" + title + "\t" + """ + entry + "\t" + descr + "\n")
            outp2.write("(" + nbr + ")\t" + list_type + "\t" + title + "\t" + entry + "\t" + descr + "\n")

    print(str(cnt) + " list entries written to ListsbyTypeAndTitle.csv")
    outp1.close()
    outp2.close()

    return lvl2


def get_list_with_loop(cookies: Any, search_title: str) -> Mapping[str, Any] | None:
    if "https" in rest_url.lower():
        lists = requests.get(rest_url + "list", cookies=cookies, verify=False)
    else:
        lists = requests.get(rest_url + "list", cookies=cookies)
    if lists.status_code != 200:
        print("received " + str(lists.status_code))
        return None

    lists_dict = xmltodict.parse(lists.content)

    lvl2 = lists_dict["feed"]["entry"]
    print(str(len(lvl2)) + " lists returned")
    list_cnt = 0

    for entry in lvl2:
        list_cnt += 1
        title = entry["title"]
        if title == search_title:
            print(search_title + " found")
            return entry

    return None  # searchTitle not found


def get_list(cookies: Any, search_title: str) -> Any:
    if "https" in rest_url.lower():
        list_response = requests.get(rest_url + "list?name=" + search_title, cookies=cookies, verify=False)
    else:
        list_response = requests.get(rest_url + "list?name=" + search_title, cookies=cookies)
    if list_response.status_code != 200:
        print("received " + str(list_response.status_code))
        return None  # searchTitle not found

    list_dict = xmltodict.parse(list_response.content)
    if "entry" in list_dict["feed"]:
        lvl2 = list_dict["feed"]["entry"]
    else:
        lvl2 = None
    return lvl2


def get_list_values(cookies: Any, entry: Mapping[str, Any]) -> list[Any]:
    list_link = entry["link"]["@href"]
    list_xml = requests.get(list_link, cookies=cookies)

    list_dict = xmltodict.parse(list_xml.content)
    content = list_dict["entry"]["content"]["list"]["content"]
    if content is None:
        return []
    list_entries = content["listEntry"]
    if "entry" in list_entries:  # then we have only one entry in the list.We have to build a list
        list_single = [list_entries]
        return list_single
    return list_entries

    # main program


if __name__ == "__main__":
    prepare_log_and_more()
    read_swg_docu_ini_file()

    urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
    if "https" in rest_url.lower():
        res = requests.post(rest_url + "login", auth=(auth_user, auth_pw), verify=False)
    else:
        res = requests.post(rest_url + "login", auth=(auth_user, auth_pw))
    if res.status_code != 200:
        append_log_dbg("Login error " + str(res.status_code) + " " + res.text)
        print("Login error " + str(res.status_code) + " " + res.text)
        exit_all(1)

    cookies = res.cookies
    c = pprint.pformat(cookies, indent=2, width=256)
    append_log_dbg("cookies: " + c)

    document_rule_sets_and_rules(cookies)
    ###listsDict = documentLists(cookies)

    if "https" in rest_url.lower():
        res = requests.post(rest_url + "logout", cookies=cookies, verify=False)
    else:
        res = requests.post(rest_url + "logout", cookies=cookies)
    append_log_dbg("main: logout " + str(res.status_code) + " for " + res.url)
