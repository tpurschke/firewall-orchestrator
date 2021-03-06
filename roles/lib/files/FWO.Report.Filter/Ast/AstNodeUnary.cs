﻿using System;
using System.Collections.Generic;
using System.Text;

namespace FWO.Report.Filter.Ast
{
    class AstNodeUnary : AstNode
    {
        public TokenKind Kind { get; set; }

        public AstNode Value { get; set; }

        public override void Extract(ref DynGraphqlQuery query)
        {

            switch (Kind)
            {
                case TokenKind.Not:
                    query.ruleWhereStatement += "_not: {";
                    break;
                default:
                    throw new Exception("### Parser Error: Expected Filtername Token (and thought there is one) ###");
            }
            Value.Extract(ref query);
            query.ruleWhereStatement += "}";
            return;
        }
    }
}
