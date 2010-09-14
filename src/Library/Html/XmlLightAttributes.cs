﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Web;

namespace CSharpTest.Net.Html
{
	/// <summary>
	/// A collection of attributes for an element
	/// </summary>
	public class XmlLightAttributes : IEnumerable<KeyValuePair<string, string>>
	{
		private readonly Dictionary<string, XmlLightAttribute> _attributes;
		
		internal XmlLightAttributes (IEnumerable<XmlLightAttribute> list)
		{
			_attributes = new Dictionary<string, XmlLightAttribute>(StringComparer.OrdinalIgnoreCase);
			int index = 0;
			foreach (XmlLightAttribute attribute in list)
			{
				XmlLightAttribute a = attribute;
				a.Ordinal = index ++;
				_attributes.Add(a.Name, a);
			}
		}
		/// <summary>
		/// Returns the number of items in the collection.
		/// </summary>
		public int Count { get { return _attributes.Count; } }
		/// <summary>
		/// Gets or Sets the attribute's unencoded text value
		/// </summary>
		public string this[string name]
		{
			get
			{
				return HttpUtility.HtmlDecode(_attributes[name].Value);
			}
			set
			{
				XmlLightAttribute a;
				if (!_attributes.TryGetValue(name, out a))
				{
					a = new XmlLightAttribute(name);
					a.Ordinal = _attributes.Count;
				}
				else if (a.Quote == XmlQuoteStyle.None || a.Quote == XmlQuoteStyle.Missing)
					a.Quote = XmlQuoteStyle.Double;
				a.Value = HttpUtility.HtmlAttributeEncode(value);
				_attributes[name] = a;
			}
		}

		/// <summary> Returns true if hte attribute is defined </summary>
		public bool ContainsKey(string name)
		{ return _attributes.ContainsKey(name); }

		/// <summary>
		/// Returns the names of the attributes in appearance order
		/// </summary>
		public IEnumerable<string> Keys
		{ get { foreach (XmlLightAttribute a in ByOrdinal) yield return a.Name; } }

		/// <summary>
		/// Adds a new attribute to the collection
		/// </summary>
        public void Add(string name, string value)
        {
        	Check.Assert<ArgumentOutOfRangeException>(_attributes.ContainsKey(name) == false);
        	this[name] = value;
        }

		/// <summary>
		/// Removes an item from the collection
		/// </summary>
		public bool Remove(string name)
		{
			if(_attributes.Remove(name))
			{
				int index = 0;
				foreach (XmlLightAttribute attr in ByOrdinal)
				{
					XmlLightAttribute a = attr;
					a.Ordinal = index++;
					_attributes[a.Name] = a;
				}
				return true;
			}
			return false;
		}

		private List<XmlLightAttribute> ByOrdinal
		{
			get
			{
				List<XmlLightAttribute> all = new List<XmlLightAttribute>(_attributes.Values);
				all.Sort(delegate(XmlLightAttribute a1, XmlLightAttribute a2) { return a1.Ordinal.CompareTo(a2.Ordinal); });
				return all;
			}
		}

		/// <summary>
		/// Returns the attributes as a collection
		/// </summary>
		/// <returns></returns>
		public XmlLightAttribute[] ToArray()
		{
			return ByOrdinal.ToArray();
		}

		/// <summary>
		/// Returns an enumerator that iterates through the collection.
		/// </summary>
		public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
		{
			foreach (XmlLightAttribute a in ByOrdinal)
				yield return new KeyValuePair<string, string>(a.Name, HttpUtility.HtmlDecode(a.Value));
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{ return this.GetEnumerator(); }
	}
}