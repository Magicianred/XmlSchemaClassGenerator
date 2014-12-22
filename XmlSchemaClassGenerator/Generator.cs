﻿using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace XmlSchemaClassGenerator
{
    public class Generator
    {
        public NamespaceProvider NamespaceProvider { get; set; }
        public string OutputFolder { get; set; }
        public Action<string> Log { get; set; }
        /// <summary>
        /// Enable data binding with INotifyPropertyChanged
        /// </summary>
        public bool EnableDataBinding { get; set; }
        /// <summary>
        /// Use XElement instead of XmlElement for Any nodes?
        /// </summary>
        public bool UseXElementForAny { get; set; }
        /// <summary>
        /// How are the names of the created properties changed?
        /// </summary>
        public NamingScheme NamingScheme { get; set; }
        /// <summary>
        /// Emit the "Order" attribute value for XmlElementAttribute to ensure the correct order
        /// of the serialized XML elements.
        /// </summary>
        public bool EmitOrder { get; set; }

        /// <summary>
        /// Determines the kind of annotations to emit
        /// </summary>
        public DataAnnotationMode DataAnnotationMode
        {
            get { return RestrictionModel.DataAnnotationMode; }
            set { RestrictionModel.DataAnnotationMode = value; }
        }

        public bool GenerateNullables
        {
            get
            {
                return PropertyModel.GenerateNullables;
            }

            set
            {
                PropertyModel.GenerateNullables = value;
            }
        }

        public bool GenerateSerializableAttribute
        {
            get { return TypeModel.GenerateSerializableAttribute; }
            set { TypeModel.GenerateSerializableAttribute = value; }
        }

        public bool GenerateDesignerCategoryAttribute
        {
            get { return ClassModel.GenerateDesignerCategoryAttribute; }
            set { ClassModel.GenerateDesignerCategoryAttribute = value; }
        }

        public Type CollectionType
        {
            get { return SimpleModel.CollectionType; }
            set { SimpleModel.CollectionType = value; }
        }

        public Type CollectionImplementationType
        {
            get { return SimpleModel.CollectionImplementationType; }
            set { SimpleModel.CollectionImplementationType = value; }
        }

        public Type IntegerDataType
        {
            get { return SimpleModel.IntegerDataType; }
            set { SimpleModel.IntegerDataType = value; }
        }

        private readonly XmlSchemaSet Set = new XmlSchemaSet();
        private Dictionary<XmlQualifiedName, XmlSchemaAttributeGroup> AttributeGroups;
        private readonly Dictionary<NamespaceKey, NamespaceModel> Namespaces = new Dictionary<NamespaceKey, NamespaceModel>();
        private Dictionary<XmlQualifiedName, TypeModel> Types = new Dictionary<XmlQualifiedName, TypeModel>();
        private static readonly XmlQualifiedName AnyType = new XmlQualifiedName("anyType", XmlSchema.Namespace);

        public Generator()
        {
            NamingScheme = NamingScheme.PascalCase;
            NamespaceProvider = new NamespaceProvider();
        }

        public void Generate(IEnumerable<string> files)
        {
            var schemas = files.Select(f => XmlSchema.Read(XmlReader.Create(f, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore }), (s, e) =>
            {
                Trace.TraceError(e.Message);
            }));

            foreach (var s in schemas)
            {
                Set.Add(s);
            }

            Set.Compile();

            BuildModel();

            var namespaces = GenerateCode();

            var provider = new Microsoft.CSharp.CSharpCodeProvider();

            foreach (var ns in namespaces)
            {
                var compileUnit = new CodeCompileUnit();
                compileUnit.Namespaces.Add(ns);

                var title = ((AssemblyTitleAttribute)Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(),
                    typeof(AssemblyTitleAttribute))).Title;
                var version = Assembly.GetExecutingAssembly().GetName().Version.ToString();

                ns.Comments.Add(new CodeCommentStatement(string.Format("This code was generated by {0} version {1}.", title, version)));

                using (StringWriter sw = new StringWriter())
                {
                    provider.GenerateCodeFromCompileUnit(compileUnit, sw, new CodeGeneratorOptions { VerbatimOrder = true, BracingStyle = "C" });
                    var s = sw.ToString().Replace("};", "}"); // remove ';' at end of automatic properties
                    var path = Path.Combine(OutputFolder, ns.Name + ".cs");
                    if (Log != null) Log(path);
                    File.WriteAllText(path, s);
                }
            }
        }

        private IEnumerable<CodeNamespace> GenerateCode()
        {
            var hierarchy = NamespaceHierarchyItem.Build(Namespaces.Values.GroupBy(x => x.Name).SelectMany(x => x))
                .MarkAmbiguousNamespaceTypes();
            return hierarchy.Flatten()
                .Select(nhi => NamespaceModel.Generate(nhi.FullName, nhi.Models));
        }

        private string BuildNamespace(Uri source, string xmlNamespace)
        {
            var key = new NamespaceKey(source, xmlNamespace);
            var result = NamespaceProvider.FindNamespace(key);
            if (!string.IsNullOrEmpty(result))
                return result;
            
            throw new Exception(string.Format("Namespace {0} not provided through map or generator.", xmlNamespace));
        }

        private static readonly Dictionary<char, string> InvalidChars = CreateInvalidChars();

        private static Dictionary<char, string> CreateInvalidChars()
        {
            var invalidChars = new Dictionary<char, string>();

            invalidChars['\x00'] = "Null";
            invalidChars['\x01'] = "StartOfHeading";
            invalidChars['\x02'] = "StartOfText";
            invalidChars['\x03'] = "EndOfText";
            invalidChars['\x04'] = "EndOfTransmission";
            invalidChars['\x05'] = "Enquiry";
            invalidChars['\x06'] = "Acknowledge";
            invalidChars['\x07'] = "Bell";
            invalidChars['\x08'] = "Backspace";
            invalidChars['\x09'] = "HorizontalTab";
            invalidChars['\x0A'] = "LineFeed";
            invalidChars['\x0B'] = "VerticalTab";
            invalidChars['\x0C'] = "FormFeed";
            invalidChars['\x0D'] = "CarriageReturn";
            invalidChars['\x0E'] = "ShiftOut";
            invalidChars['\x0F'] = "ShiftIn";
            invalidChars['\x10'] = "DataLinkEscape";
            invalidChars['\x11'] = "DeviceControl1";
            invalidChars['\x12'] = "DeviceControl2";
            invalidChars['\x13'] = "DeviceControl3";
            invalidChars['\x14'] = "DeviceControl4";
            invalidChars['\x15'] = "NegativeAcknowledge";
            invalidChars['\x16'] = "SynchronousIdle";
            invalidChars['\x17'] = "EndOfTransmissionBlock";
            invalidChars['\x18'] = "Cancel";
            invalidChars['\x19'] = "EndOfMedium";
            invalidChars['\x1A'] = "Substitute";
            invalidChars['\x1B'] = "Escape";
            invalidChars['\x1C'] = "FileSeparator";
            invalidChars['\x1D'] = "GroupSeparator";
            invalidChars['\x1E'] = "RecordSeparator";
            invalidChars['\x1F'] = "UnitSeparator";
            //invalidChars['\x20'] = "Space";
            invalidChars['\x21'] = "ExclamationMark";
            invalidChars['\x22'] = "Quote";
            invalidChars['\x23'] = "Hash";
            invalidChars['\x24'] = "Dollar";
            invalidChars['\x25'] = "Percent";
            invalidChars['\x26'] = "Ampersand";
            invalidChars['\x27'] = "SingleQuote";
            invalidChars['\x28'] = "LeftParenthesis";
            invalidChars['\x29'] = "RightParenthesis";
            invalidChars['\x2A'] = "Asterisk";
            invalidChars['\x2B'] = "Plus";
            invalidChars['\x2C'] = "Comma";
            //invalidChars['\x2D'] = "Minus";
            invalidChars['\x2E'] = "Period";
            invalidChars['\x2F'] = "Slash";
            invalidChars['\x3A'] = "Colon";
            invalidChars['\x3B'] = "Semicolon";
            invalidChars['\x3C'] = "LessThan";
            invalidChars['\x3D'] = "Equal";
            invalidChars['\x3E'] = "GreaterThan";
            invalidChars['\x3F'] = "QuestionMark";
            invalidChars['\x40'] = "At";
            invalidChars['\x5B'] = "LeftSquareBracket";
            invalidChars['\x5C'] = "Backslash";
            invalidChars['\x5D'] = "RightSquareBracket";
            invalidChars['\x5E'] = "Caret";
            //invalidChars['\x5F'] = "Underscore";
            invalidChars['\x60'] = "Backquote";
            invalidChars['\x7B'] = "LeftCurlyBrace";
            invalidChars['\x7C'] = "Pipe";
            invalidChars['\x7D'] = "RightCurlyBrace";
            invalidChars['\x7E'] = "Tilde";
            invalidChars['\x7F'] = "Delete";

            return invalidChars;
        }

        private static readonly Regex InvalidCharsRegex = CreateInvalidCharsRegex();

        private static Regex CreateInvalidCharsRegex()
        {
            var r = string.Join("", InvalidChars.Keys.Select(c => string.Format(@"\x{0:x2}", (int)c)).ToArray());
            return new Regex("[" + r + "]", RegexOptions.Compiled);
        }

        private static readonly CodeDomProvider Provider = new Microsoft.CSharp.CSharpCodeProvider();
        public static string MakeValidIdentifier(string s)
        {
            var id = InvalidCharsRegex.Replace(s, m => InvalidChars[m.Value[0]]);
            return Provider.CreateValidIdentifier(Regex.Replace(id, @"\W+", "_"));
        }

        public string ToTitleCase(string s)
        {
            return ToTitleCase(s, NamingScheme);
        }

        public static string ToTitleCase(string s, NamingScheme namingScheme)
        {
            if (string.IsNullOrEmpty(s)) return s;
            switch (namingScheme)
            {
                case NamingScheme.PascalCase:
                    s = s.ToPascalCase();
                    break;
            }
            return MakeValidIdentifier(s);
        }

        private void BuildModel()
        {
            var objectModel = new SimpleModel
            {
                Name = "AnyType",
                Namespace = CreateNamespaceModel(new Uri(XmlSchema.Namespace), AnyType),
                XmlSchemaName = AnyType,
                XmlSchemaType = null,
                ValueType = typeof(object),
                UseDataTypeAttribute = false
            };

            Types[AnyType] = objectModel;

            AttributeGroups = Set.Schemas().Cast<XmlSchema>().SelectMany(s => s.AttributeGroups.Values.Cast<XmlSchemaAttributeGroup>()).ToDictionary(g => g.QualifiedName);

            foreach (var rootElement in Set.GlobalElements.Values.Cast<XmlSchemaElement>())
            {
                var source = new Uri(rootElement.GetSchema().SourceUri);
                var qualifiedName = rootElement.ElementSchemaType.QualifiedName;
                if (qualifiedName.IsEmpty) qualifiedName = rootElement.QualifiedName;
                var type = CreateTypeModel(source, rootElement.ElementSchemaType, qualifiedName);

                if (type.RootElementName != null)
                {
                    if (type is ClassModel)
                    {
                        // There is already another global element with this type.
                        // Need to create an empty derived class.

                        var derivedClassModel = new ClassModel
                        {
                            Name = ToTitleCase(rootElement.QualifiedName.Name),
                            Namespace = CreateNamespaceModel(source, rootElement.QualifiedName)
                        };

                        derivedClassModel.Documentation.AddRange(GetDocumentation(rootElement));

                        if (derivedClassModel.Namespace != null)
                        {
                            derivedClassModel.Name = derivedClassModel.Namespace.GetUniqueTypeName(derivedClassModel.Name);
                            derivedClassModel.Namespace.Types[derivedClassModel.Name] = derivedClassModel;
                        }

                        Types[rootElement.QualifiedName] = derivedClassModel;

                        derivedClassModel.BaseClass = (ClassModel)type;
                        ((ClassModel)derivedClassModel.BaseClass).DerivedTypes.Add(derivedClassModel);

                        derivedClassModel.RootElementName = rootElement.QualifiedName;
                    }
                    else
                    {
                        Types[rootElement.QualifiedName] = type;
                    }
                }
                else
                {
                    var classModel = type as ClassModel;
                    if (classModel != null)
                    {
                        classModel.Documentation.AddRange(GetDocumentation(rootElement));
                    }

                    type.RootElementName = rootElement.QualifiedName;
                }
            }

            foreach (var globalType in Set.GlobalTypes.Values.Cast<XmlSchemaType>())
            {
                var schema = globalType.GetSchema();
                var source = (schema == null ? null : new Uri(schema.SourceUri));
                var type = CreateTypeModel(source, globalType);
            }
        }

        private IEnumerable<XmlSchemaAttribute> GetAttributes(XmlSchemaObjectCollection objects)
        {
            if (objects == null) yield break;

            foreach (var a in objects.OfType<XmlSchemaAttribute>())
            {
                yield return a;
            }

            foreach (var r in objects.OfType<XmlSchemaAttributeGroupRef>())
            {
                foreach (var a in GetAttributes(AttributeGroups[r.RefName].Attributes))
                {
                    yield return a;
                }
            }
        }

        // see http://msdn.microsoft.com/en-us/library/z2w0sxhf.aspx
        private static readonly HashSet<string> EnumTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
            { "string", "normalizedString", "token", "Name", "NCName", "ID", "ENTITY", "NMTOKEN" };

        // ReSharper disable once FunctionComplexityOverflow
        private TypeModel CreateTypeModel(Uri source, XmlSchemaType type, XmlQualifiedName qualifiedName = null)
        {
            if (qualifiedName == null) qualifiedName = type.QualifiedName;

            TypeModel typeModel;
            if (!qualifiedName.IsEmpty && Types.TryGetValue(qualifiedName, out typeModel)) return typeModel;

            if (source == null)
                throw new ArgumentNullException("source");
            var namespaceModel = CreateNamespaceModel(source, qualifiedName);

            var docs = GetDocumentation(type);

            var complexType = type as XmlSchemaComplexType;
            if (complexType != null)
            {
                var name = ToTitleCase(qualifiedName.Name);
                if (namespaceModel != null) name = namespaceModel.GetUniqueTypeName(name);

                var classModel = new ClassModel
                {
                    Name = name,
                    Namespace = namespaceModel,
                    XmlSchemaName = qualifiedName,
                    XmlSchemaType = type,
                    IsAbstract = complexType.IsAbstract,
                    IsAnonymous = type.QualifiedName.Name == "",
                    EnableDataBinding = EnableDataBinding,
                };

                classModel.Documentation.AddRange(docs);

                if (namespaceModel != null)
                {
                    namespaceModel.Types[classModel.Name] = classModel;
                }

                if (!qualifiedName.IsEmpty) Types[qualifiedName] = classModel;

                if (complexType.BaseXmlSchemaType != null && complexType.BaseXmlSchemaType.QualifiedName != AnyType)
                {
                    var baseModel = CreateTypeModel(source, complexType.BaseXmlSchemaType);
                    classModel.BaseClass = baseModel;
                    if (baseModel is ClassModel) ((ClassModel)classModel.BaseClass).DerivedTypes.Add(classModel);
                }

                XmlSchemaParticle particle = null;
                if (classModel.BaseClass != null)
                {
                    if (complexType.ContentModel.Content is XmlSchemaComplexContentExtension)
                        particle = ((XmlSchemaComplexContentExtension)complexType.ContentModel.Content).Particle;
                    else if (complexType.ContentModel.Content is XmlSchemaComplexContentRestriction)
                        particle = ((XmlSchemaComplexContentRestriction)complexType.ContentModel.Content).Particle;
                }
                else particle = complexType.ContentTypeParticle;

                var items = GetElements(particle);

                var order = 0;
                foreach (var item in items)
                {
                    PropertyModel property = null;

                    var element = item.XmlParticle as XmlSchemaElement;
                    // ElementSchemaType must be non-null. This is not the case when maxOccurs="0".
                    if (element != null && element.ElementSchemaType != null)
                    {
                        var elementQualifiedName = element.ElementSchemaType.QualifiedName;

                        if (elementQualifiedName.IsEmpty)
                        {
                            elementQualifiedName = element.QualifiedName;

                            if (elementQualifiedName.IsEmpty || elementQualifiedName.Namespace == "")
                            {
                                // inner type, have to generate a type name
                                var typeName = ToTitleCase(classModel.Name) + ToTitleCase(element.QualifiedName.Name);
                                elementQualifiedName = new XmlQualifiedName(typeName, qualifiedName.Namespace);
                                // try to avoid name clashes
                                if (NameExists(elementQualifiedName)) elementQualifiedName = new[] { "Item", "Property", "Element" }
                                    .Select(s => new XmlQualifiedName(elementQualifiedName.Name + s, elementQualifiedName.Namespace))
                                    .First(n => !NameExists(n));
                            }
                        }

                        var propertyName = ToTitleCase(element.QualifiedName.Name);
                        if (propertyName == classModel.Name) propertyName += "Property"; // member names cannot be the same as their enclosing type

                        property = new PropertyModel
                        {
                            OwningType = classModel,
                            XmlSchemaName = element.QualifiedName,
                            Name = propertyName,
                            Type = CreateTypeModel(source, element.ElementSchemaType, elementQualifiedName),
                            IsNillable = element.IsNillable,
                            IsNullable = item.MinOccurs < 1.0m,
                            IsCollection = item.MaxOccurs > 1.0m || particle.MaxOccurs > 1.0m, // http://msdn.microsoft.com/en-us/library/vstudio/d3hx2s7e(v=vs.100).aspx
                            DefaultValue = element.DefaultValue,
                            Form = element.Form == XmlSchemaForm.None ? element.GetSchema().ElementFormDefault : element.Form,
                            XmlNamespace = element.QualifiedName.Namespace != "" && element.QualifiedName.Namespace != qualifiedName.Namespace ? element.QualifiedName.Namespace : null,
                        };
                    }
                    else
                    {
                        var any = item.XmlParticle as XmlSchemaAny;
                        if (any != null)
                        {
                            property = new PropertyModel
                            {
                                OwningType = classModel,
                                Name = "Any",
                                Type = new SimpleModel { ValueType = (UseXElementForAny ? typeof(XElement) : typeof(XmlElement)), UseDataTypeAttribute = false },
                                IsNullable = item.MinOccurs < 1.0m,
                                IsCollection = item.MaxOccurs > 1.0m || particle.MaxOccurs > 1.0m, // http://msdn.microsoft.com/en-us/library/vstudio/d3hx2s7e(v=vs.100).aspx
                                IsAny = true,
                            };
                        }
                    }

                    if (property != null)
                    {
                        var itemDocs = GetDocumentation(item.XmlParticle);
                        property.Documentation.AddRange(itemDocs);

                        if (EmitOrder)
                            property.Order = order++;
                        property.IsDeprecated = itemDocs.Any(d => d.Text.StartsWith("DEPRECATED"));

                        classModel.Properties.Add(property);
                    }
                }

                XmlSchemaObjectCollection attributes = null;
                if (classModel.BaseClass != null)
                {
                    if (complexType.ContentModel.Content is XmlSchemaComplexContentExtension)
                        attributes = ((XmlSchemaComplexContentExtension)complexType.ContentModel.Content).Attributes;
                    else if (complexType.ContentModel.Content is XmlSchemaSimpleContentExtension)
                        attributes = ((XmlSchemaSimpleContentExtension)complexType.ContentModel.Content).Attributes;
                    else if (complexType.ContentModel.Content is XmlSchemaComplexContentRestriction)
                        attributes = ((XmlSchemaComplexContentRestriction)complexType.ContentModel.Content).Attributes;
                    else if (complexType.ContentModel.Content is XmlSchemaSimpleContentRestriction)
                        attributes = ((XmlSchemaSimpleContentRestriction)complexType.ContentModel.Content).Attributes;
                }
                else attributes = complexType.Attributes;

                foreach (var attribute in GetAttributes(attributes).Where(a => a.Use != XmlSchemaUse.Prohibited))
                {
                    var attributeQualifiedName = attribute.AttributeSchemaType.QualifiedName;

                    if (attributeQualifiedName.IsEmpty)
                    {
                        attributeQualifiedName = attribute.QualifiedName;

                        if (attributeQualifiedName.IsEmpty || attributeQualifiedName.Namespace == "")
                        {
                            // inner type, have to generate a type name
                            var typeName = ToTitleCase(classModel.Name) + ToTitleCase(attribute.QualifiedName.Name);
                            attributeQualifiedName = new XmlQualifiedName(typeName, qualifiedName.Namespace);
                            // try to avoid name clashes
                            if (NameExists(attributeQualifiedName)) attributeQualifiedName = new[] { "Item", "Property", "Element" }
                                .Select(s => new XmlQualifiedName(attributeQualifiedName.Name + s, attributeQualifiedName.Namespace))
                                .First(n => !NameExists(n));
                        }
                    } 
                    
                    var attributeName = ToTitleCase(attribute.QualifiedName.Name);
                    if (attributeName == classModel.Name) attributeName += "Property"; // member names cannot be the same as their enclosing type

                    var property = new PropertyModel
                    {
                        OwningType = classModel,
                        Name = attributeName,
                        XmlSchemaName = attribute.QualifiedName,
                        Type = CreateTypeModel(source, attribute.AttributeSchemaType, attributeQualifiedName),
                        IsAttribute = true,
                        IsNullable = attribute.Use != XmlSchemaUse.Required,
                        DefaultValue = attribute.DefaultValue,
                        Form = attribute.Form == XmlSchemaForm.None ? attribute.GetSchema().AttributeFormDefault : attribute.Form,
                        XmlNamespace = attribute.QualifiedName.Namespace != "" && attribute.QualifiedName.Namespace != qualifiedName.Namespace ? attribute.QualifiedName.Namespace : null,
                    };

                    var attributeDocs = GetDocumentation(attribute);
                    property.Documentation.AddRange(attributeDocs);

                    classModel.Properties.Add(property);
                }

                if (complexType.AnyAttribute != null)
                {
                    var property = new PropertyModel
                    {
                        OwningType = classModel,
                        Name = "AnyAttribute",
                        Type = new SimpleModel { ValueType = typeof(XmlAttribute), UseDataTypeAttribute = false },
                        IsAttribute = true,
                        IsCollection = true,
                        IsAny = true
                    };

                    var attributeDocs = GetDocumentation(complexType.AnyAttribute);
                    property.Documentation.AddRange(attributeDocs);

                    classModel.Properties.Add(property);
                }

                return classModel;
            }

            var simpleType = type as XmlSchemaSimpleType;
            if (simpleType != null)
            {
                var restrictions = new List<RestrictionModel>();

                var typeRestriction = simpleType.Content as XmlSchemaSimpleTypeRestriction;
                if (typeRestriction != null)
                {
                    var enumFacets = typeRestriction.Facets.OfType<XmlSchemaEnumerationFacet>().ToList();
                    var isEnum = (enumFacets.Count == typeRestriction.Facets.Count && enumFacets.Count != 0)
                                    || (EnumTypes.Contains(typeRestriction.BaseTypeName.Name) && enumFacets.Any());
                    if (isEnum)
                    {
                        // we got an enum
                        var name = ToTitleCase(qualifiedName.Name);
                        if (namespaceModel != null) name = namespaceModel.GetUniqueTypeName(name);

                        var enumModel = new EnumModel
                        {
                            Name = name,
                            Namespace = namespaceModel,
                            XmlSchemaName = qualifiedName,
                            XmlSchemaType = type,
                        };

                        enumModel.Documentation.AddRange(docs);

                        foreach (var facet in enumFacets)
                        {
                            var value = new EnumValueModel
                            {
                                Name = ToTitleCase(facet.Value).ToNormalizedEnumName(),
                                Value = facet.Value
                            };

                            var valueDocs = GetDocumentation(facet);
                            value.Documentation.AddRange(valueDocs);

                            var deprecated = facet.Annotation != null && facet.Annotation.Items.OfType<XmlSchemaAppInfo>()
                                .Any(a => a.Markup.Any(m => m.Name == "annox:annotate" && m.HasChildNodes && m.FirstChild.Name == "jl:Deprecated"));
                            value.IsDeprecated = deprecated;

                            enumModel.Values.Add(value);
                        }

                        if (namespaceModel != null)
                        {
                            namespaceModel.Types[enumModel.Name] = enumModel;
                        }

                        if (!qualifiedName.IsEmpty) Types[qualifiedName] = enumModel;

                        return enumModel;
                    }

                    restrictions = typeRestriction.Facets.Cast<XmlSchemaFacet>().Select(f => GetRestriction(simpleType, f)).Where(r => r != null).Sanitize().ToList();
                }

                var simpleModelName = ToTitleCase(qualifiedName.Name);
                if (namespaceModel != null) simpleModelName = namespaceModel.GetUniqueTypeName(simpleModelName);

                var simpleModel = new SimpleModel
                {
                    Name = simpleModelName,
                    Namespace = namespaceModel,
                    XmlSchemaName = qualifiedName,
                    XmlSchemaType = type,
                    ValueType = simpleType.Datatype.GetEffectiveType(),
                };

                simpleModel.Documentation.AddRange(docs);
                simpleModel.Restrictions.AddRange(restrictions);

                if (namespaceModel != null)
                {
                    namespaceModel.Types[simpleModel.Name] = simpleModel;
                }

                if (!qualifiedName.IsEmpty) Types[qualifiedName] = simpleModel;

                return simpleModel;
            }

            throw new Exception(string.Format("Cannot build declaration for {0}", qualifiedName));
        }

        private NamespaceModel CreateNamespaceModel(Uri source, XmlQualifiedName qualifiedName)
        {
            NamespaceModel namespaceModel = null;
            if (!qualifiedName.IsEmpty && qualifiedName.Namespace != XmlSchema.Namespace)
            {
                var key = new NamespaceKey(source, qualifiedName.Namespace);
                if (!Namespaces.TryGetValue(key, out namespaceModel))
                {
                    var namespaceName = BuildNamespace(source, qualifiedName.Namespace);
                    namespaceModel = new NamespaceModel(key) { Name = namespaceName };
                    Namespaces.Add(key, namespaceModel);
                }
            }
            return namespaceModel;
        }

        private bool NameExists(XmlQualifiedName name)
        {
            var elements = Set.GlobalElements.Names.Cast<XmlQualifiedName>();
            var types = Set.GlobalTypes.Names.Cast<XmlQualifiedName>();
            return elements.Concat(types).Any(n => n.Namespace == name.Namespace && name.Name.Equals(n.Name, StringComparison.OrdinalIgnoreCase));
        }

        private RestrictionModel GetRestriction(XmlSchemaSimpleType type, XmlSchemaFacet facet)
        {
            if (facet is XmlSchemaMaxLengthFacet)
                return new MaxLengthRestrictionModel { Value = int.Parse(facet.Value) };
            if (facet is XmlSchemaMinLengthFacet)
                return new MinLengthRestrictionModel { Value = int.Parse(facet.Value) };
            if (facet is XmlSchemaTotalDigitsFacet)
                return new TotalDigitsRestrictionModel { Value = int.Parse(facet.Value) };
            if (facet is XmlSchemaFractionDigitsFacet)
                return new FractionDigitsRestrictionModel { Value = int.Parse(facet.Value) };

            if (facet is XmlSchemaPatternFacet)
                return new PatternRestrictionModel { Value = facet.Value };

            var valueType = type.Datatype.ValueType;

            if (facet is XmlSchemaMinInclusiveFacet)
                return new MinInclusiveRestrictionModel { Value = facet.Value, Type = valueType };
            if (facet is XmlSchemaMinExclusiveFacet)
                return new MinExclusiveRestrictionModel { Value = facet.Value, Type = valueType };
            if (facet is XmlSchemaMaxInclusiveFacet)
                return new MaxInclusiveRestrictionModel { Value = facet.Value, Type = valueType };
            if (facet is XmlSchemaMaxExclusiveFacet)
                return new MaxExclusiveRestrictionModel { Value = facet.Value, Type = valueType };

            // unsupported restriction
            return null;
        }

        public IEnumerable<Particle> GetElements(XmlSchemaGroupBase groupBase)
        {
            foreach (var item in groupBase.Items)
            {
                foreach (var element in GetElements(item))
                {
                    element.MaxOccurs = Math.Max(element.MaxOccurs, groupBase.MaxOccurs);
                    element.MinOccurs = Math.Min(element.MinOccurs, groupBase.MinOccurs);
                    yield return element;
                }
            }
        }

        public IEnumerable<Particle> GetElements(XmlSchemaObject item)
        {
            if (item == null) yield break;

            var element = item as XmlSchemaElement;
            if (element != null) yield return new Particle(element);

            var any = item as XmlSchemaAny;
            if (any != null) yield return new Particle(any);

            var groupRef = item as XmlSchemaGroupRef;
            if (groupRef != null)
                foreach (var groupRefElement in GetElements(groupRef.Particle))
                    yield return groupRefElement;

            var itemGroupBase = item as XmlSchemaGroupBase;
            if (itemGroupBase != null)
                foreach (var groupBaseElement in GetElements(itemGroupBase))
                    yield return groupBaseElement;
        }

        public List<DocumentationModel> GetDocumentation(XmlSchemaAnnotated annotated)
        {
            if (annotated.Annotation == null) return new List<DocumentationModel>();

            return annotated.Annotation.Items.OfType<XmlSchemaDocumentation>()
                .Where(d => d.Markup != null && d.Markup.Any())
                .Select(d => new DocumentationModel { Language = d.Language, Text = new XText(d.Markup.First().InnerText).ToString() })
                .Where(d => !string.IsNullOrEmpty(d.Text))
                .ToList();
        }
    }
}
