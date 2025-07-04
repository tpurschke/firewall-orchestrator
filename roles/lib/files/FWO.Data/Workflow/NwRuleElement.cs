﻿namespace FWO.Data.Workflow
{
    public class NwRuleElement
    {
        public long ElemId { get; set; }
        public long TaskId { get; set; }
        public string RuleUid { get; set; } = "";
        public string? Name { get; set; } = "";



        public WfReqElement ToReqElement()
        {
            WfReqElement element = new()
            {
                Id = ElemId,
                TaskId = TaskId,
                Field = ElemFieldType.rule.ToString(),
                RuleUid = RuleUid,
                Name = Name
            };
            return element;
        }

        public WfImplElement ToImplElement()
        {
            WfImplElement element = new()
            {
                Id = ElemId,
                ImplTaskId = TaskId,
                Field = ElemFieldType.rule.ToString(),
                RuleUid = RuleUid,
                Name = Name
            };
            return element;
        }
    }
}
