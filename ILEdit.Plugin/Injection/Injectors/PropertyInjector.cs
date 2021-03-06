﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ILEdit.Injection;
using ICSharpCode.ILSpy.TreeNodes;
using Mono.Cecil;
using System.Windows;
using Mono.Cecil.Cil;

namespace ILEdit.Injection.Injectors
{
    /// <summary>
    /// Property injector
    /// </summary>
    public class PropertyInjector : IInjector
    {
        #region Properties

        public string Name
        {
            get { return "Property"; }
        }

        public string Description
        {
            get { return "Injects a new property, its relative get and set methods and the backing field"; }
        }

        public System.Windows.Media.ImageSource Icon
        {
            get { return new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/ILSpy;component/Images/Property.png")); }
        }

        public bool NeedsMember
        {
            get { return true; }
        }

        public Predicate<Mono.Cecil.IMetadataTokenProvider> MemberFilter
        {
            get { return MemberFilters.Types; }
        }

        public TokenType[] SelectableMembers
        {
            get { return new TokenType[] { TokenType.TypeDef }; }
        }

        #endregion

        public bool CanInjectInNode(ICSharpCode.ILSpy.TreeNodes.ILSpyTreeNode node)
        {
            //Try-cast
            var memberNode = node as IMemberTreeNode;
            var type = memberNode == null ? null : (memberNode.Member as TypeDefinition);

            //Can inject only in types
            return type != null;
        }

        public void Inject(ICSharpCode.ILSpy.TreeNodes.ILSpyTreeNode node, string name, IMetadataTokenProvider member)
        {
            //Type node
            var type = (TypeDefinition)((IMemberTreeNode)node).Member;

            //Property type
            var propertyType = type.Module.Import((TypeReference)member, type);

            //Creates the property definition
            var prop = new PropertyDefinition(
                name,
                PropertyAttributes.None,
                propertyType
            ) {
                MetadataToken = new MetadataToken(TokenType.Property, ILEdit.GlobalContainer.GetFreeRID(type.Module))
            };

            //Creates the backing field if we aren't injecting in an interface
            FieldReference backingField = null;
            if (!type.IsInterface)
            {
                backingField = new FieldDefinition(string.Format("<{0}>k__BackingField", name), FieldAttributes.Private, propertyType) { MetadataToken = new MetadataToken(TokenType.Field, ILEdit.GlobalContainer.GetFreeRID(type.Module)) };
                type.Fields.Add((FieldDefinition)backingField);
            }

            //Checks if the type is generic
            if (type.HasGenericParameters)
            {
                var giType = new GenericInstanceType(type);
                foreach (var x in type.GenericParameters)
                    giType.GenericArguments.Add(x);
                backingField = new FieldReference(backingField.Name, propertyType, giType);
            }

            //Creates the get method
            prop.GetMethod = new MethodDefinition(
                "get_" + name,
                MethodAttributes.Public  | MethodAttributes.SpecialName,
                propertyType
            ) { 
                IsGetter = true,
                MetadataToken = new MetadataToken(TokenType.Method, ILEdit.GlobalContainer.GetFreeRID(type.Module))
            };

            //Checks if the destination type is an interface
            if (type.IsInterface)
                prop.GetMethod.Attributes |= MethodAttributes.NewSlot | MethodAttributes.CheckAccessOnOverride | MethodAttributes.Abstract | MethodAttributes.Virtual;
            else
            {
                //Writes the instructions in the get method
                var getBody = prop.GetMethod.Body;
                getBody.MaxStackSize = 1;
                getBody.GetType().GetField("code_size", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(getBody, 1); //ILSpy doesn't decompile the method otherwise!
                var getProcessor = getBody.GetILProcessor();
                getProcessor.Emit(OpCodes.Ldarg_0);
                getProcessor.Emit(OpCodes.Ldfld, backingField);
                getProcessor.Emit(OpCodes.Ret);
            }

            //Creates the set method
            prop.SetMethod = new MethodDefinition(
                "set_" + name,
                MethodAttributes.Public | MethodAttributes.SpecialName,
                type.Module.TypeSystem.Void
            )
            {
                IsSetter = true,
                MetadataToken = new MetadataToken(TokenType.Method, ILEdit.GlobalContainer.GetFreeRID(type.Module)),
                Parameters = { 
                    new ParameterDefinition("value", ParameterAttributes.None, propertyType)
                }
            };

            //Checks if the destination type is an interface
            if (type.IsInterface)
                prop.SetMethod.Attributes |= MethodAttributes.NewSlot | MethodAttributes.CheckAccessOnOverride | MethodAttributes.Abstract | MethodAttributes.Virtual;
            else
            {
                //Writes the instructions in the set method
                var setBody = prop.SetMethod.Body;
                setBody.MaxStackSize = 8;
                setBody.GetType().GetField("code_size", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(setBody, 1);
                var setProcessor = setBody.GetILProcessor();
                setProcessor.Emit(OpCodes.Ldarg_0);
                setProcessor.Emit(OpCodes.Ldarg_1);
                setProcessor.Emit(OpCodes.Stfld, backingField);
                setProcessor.Emit(OpCodes.Ret);
            }

            //Adds the property to the type
            type.Properties.Add(prop);
            type.Methods.Add(prop.GetMethod);
            type.Methods.Add(prop.SetMethod);
            prop.GetMethod.Overrides.Clear(); //Overrides automatically added?!
            prop.SetMethod.Overrides.Clear();

            //Creates the nodes
            if (node is TypeTreeNode)
            {
                if (!type.IsInterface)
                    node.Children.Add(new ILEditTreeNode(backingField, true));
                node.Children.Add(new ILEditTreeNode(prop, false));
                Helpers.Tree.SortChildren((TypeTreeNode)node);
            }
            else if (node is ILEditTreeNode)
            {
                ((ILEditTreeNode)node).RefreshChildren();
            }
        }
    }
}
