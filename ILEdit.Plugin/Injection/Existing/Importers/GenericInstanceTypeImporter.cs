﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;

namespace ILEdit.Injection.Existing.Importers
{
    internal class GenericInstanceTypeImporter : MemberImporter
    {
        GenericInstanceType retType;

        public GenericInstanceTypeImporter(IMetadataTokenProvider member, IMetadataTokenProvider destination, MemberImportingSession session)
            : base(member, destination, session)
        {
        }

        protected override bool CanImportCore(Mono.Cecil.IMetadataTokenProvider member, Mono.Cecil.IMetadataTokenProvider destination)
        {
            return member is GenericInstanceType;
        }

        protected override void ScanCore(MemberImportingOptions options, List<MemberImporter> importList)
        {
            //Type
            var type = (GenericInstanceType)Member;

            //Element type
            var elType = type.ElementType.Resolve();
            retType = new GenericInstanceType(elType);
            var elTypeImporter = Helpers.CreateTypeImporter(elType, Session, importList, options);
            elTypeImporter.ImportFinished += t => { 
                var newType = new GenericInstanceType((TypeReference)t);
                foreach (var a in retType.GenericArguments)
                    newType.GenericArguments.Add(a);
                retType = newType;
            };

            //Throws if cancellation was requested
            options.CancellationToken.ThrowIfCancellationRequested();

            //Imports the arguments
            foreach (var a in type.GenericArguments)
            {
                var arg = a;
                if (a is GenericParameter)
                {
                    importList.Add(MemberImporter.Create((_, __) => { retType.GenericArguments.Add(arg); return null; }));
                }
                else
                {
                    var argImporter = Helpers.CreateTypeImporter(a, Session, importList, options);
                    argImporter.ImportFinished += x => retType.GenericArguments.Add((TypeReference)x);
                }
            }

            //Throws if cancellation was requested
            options.CancellationToken.ThrowIfCancellationRequested();
        }

        protected override Mono.Cecil.IMetadataTokenProvider ImportCore(MemberImportingOptions options, ICSharpCode.TreeView.SharpTreeNode node)
        {
            //Returns the type
            return retType;
        }

        protected override void DisposeCore()
        {
            retType = null;
        }
    }
}
