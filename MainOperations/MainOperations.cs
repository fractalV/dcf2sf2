using System.Collections.Specialized;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace DcfToSf2.MainOperations
{
    internal class MainOperations
    {
        public static void ConvertFile(FileInfo file)
        {
            try
            {
                int old_y = 0;

                //Итоговый список sf2 документа
                List<string> DocumentLines = [];
                //читаем файл
                string str = ReadFile(file);
                //dcf название документа
                string DocumentName = GetDocumentName(str);
                //формируем заголовок
                DocumentLines.Add($"DOCUMENT={DocumentName}");

                List<Block> ListAllBlocks = [];

                Size size = new(0, 0);
                Position position = new(x: Consts.Default_X, y: Consts.Default_Y);

                //получаем список полей с уровнями вложенности
                var data = ConvertFileToData(str);

                List<string> searchFieldNames =
                [
                    //адрес
                    "AddressKindCode",
                    "PostalCode",
                    "CountryCode",
                    "CounryName",
                    "Region",
                    "District",
                    "Town",
                    "City",
                    "StreetHouse",
                    "House",
                    "Room",
                    "AddressText",
                    "PostOfficeBoxId",
                    // "_AddressKindCode", "_PostalCode", "_CountryCode", "_CounryName", "_Region", "_District",
                    // "_Town", "_City", "_StreetHouse", "_House", "_Room", "_AddressText",
                    // "_PostOfficeBoxId",

                    //ед.изм
                    "GoodsQuantity",
                    "MeasureUnitQualifierName",
                    "MeasureUnitQualifierCode",
                    //документ
                    "CustomsDocumentCode",
                    "PrDocumentName",
                    "PrDocumentNumber",
                    "PrDocumentDate",
                    //ТО
                    "CustomsCode",
                    "RegistrationDate",
                    "GTDNumber",
                    //Подпись
                    "PersonSurname",
                    "PersonName",
                    "PersonMiddleName",
                    "PersonPost",
                    "IssueDate",
                    //Сведения об организации.
                    "OrganizationName",
                    "OrganizationLanguage",
                    "ShortName",
                    "OGRN",
                    "INN",
                    "KPP",
                    "Phone",
                    "mail",
                    "IdentityCardCode",
                    "IdentityCardName",
                    "IdentityCardSeries",
                    "IdentityCardNumber",
                    "IdentityCardDate",
                    "IssuerCode",
                    "AuthorityId",
                    //таможня
                    "Customs_Code",
                    "Customs_OfficeName",
                ];

                var searchResult = FindSequentialData(data, searchFieldNames);
                /*
                    foreach (var d in searchResult)
                    {
                        Debug.WriteLine(
                            $"Level: {d.Level}, NameBlock: {d.NameBlock}, ElementName: {d.FieldElement.ElementName}, ElementValue: {d.FieldElement.ElementValue}"
                        );
                    }
                */
                Dictionary<string, string> mapX = [];
                Dictionary<string, string> mapCoordinates = [];

                NameValueCollection NameValueCollectionPaths = [];
                Dictionary<string, string> namesBlocks = [];

                List<string> currentPath = [];

                currentPath.Add(Consts.DefaultBlockName);

                int previousLevel = 0;
                string updatedPath = Consts.DefaultBlockName;

                bool fidTemplate = false;

                string textForTemplate = "";

                foreach (var element in data)
                {
                    Block block = new() { Name = element.NameBlock };
                    Size sizeBlock = new(0, 0);
                    Position positionBlock = new(Consts.Default_X, Consts.Default_Y);
                    bool seq = element.FieldElement.ElementValue.Contains("SEQ="); //блок двухстрочный

                    if (IsServiceField(element.FieldElement.ElementName))
                        continue;

                    // if (IsBlockField(element))
                    //     continue;

                    //Если логический блок, (тип B)
                    if (IsBlockField(element))
                    {
                        Value valueText = GetValueText(element);
                        if (string.IsNullOrEmpty(valueText.Comment)) continue;

                        //пропуск неиспользуемых граф
                        if (SkipBlock(valueText.Comment.ToLower())) continue;

                        if (string.IsNullOrEmpty(currentPath.LastOrDefault()))
                            block.Name = Consts.DefaultBlockName;
                        else
                            block.Name = currentPath.Last();

                        size.Length = valueText.Comment.Length * Consts.DefaultWidthChar;
                        size.Height = Consts.DefaultHeightText + 4;
                        Field fieldText =
                            new()
                            {
                                FieldType = Type.Text,
                                FieldPosition = UpdateYCoordinate(
                                        Type.Text,
                                        positionBlock,
                                        ref mapCoordinates,
                                        0,
                                        updatedPath
                                    ),
                                FieldSize = size,
                                FieldValue = valueText,
                            };
                        //block.Name = element.NameBlock;


                        block.Fields.Add(fieldText);
                        ListAllBlocks.Add(block);

                        // UpdateYCoordinate(
                        //     Type.Text,
                        //     positionBlock,
                        //     ref mapCoordinates,
                        //     bold: 4
                        // );

                        NameValueCollectionPaths.Add(updatedPath, block.ToString());

                        continue;
                    }

                    //Если это объявление блока как nameblock{&&Num NAMESPACE="nameblock"                    
                    if (string.IsNullOrEmpty(element.FieldElement.ElementValue))
                    {
                        if (string.IsNullOrEmpty(currentPath.LastOrDefault()))
                        {
                            block.Name = Consts.DefaultBlockName;
                        }
                        else
                        {
                            if (currentPath.Count >= 1)
                            {
                                if (previousLevel <= currentPath.Count - 1)
                                {
                                    var IndexInData = data.IndexOf(element);
                                    for (int i = IndexInData; i > 0; i--)
                                    {
                                        if (data[i].Level < element.Level)
                                        {
                                            var lvl = data[i].Level;
                                            if (lvl == 0)
                                            {
                                                block.Name = Consts.DefaultBlockName;
                                            }
                                            else
                                            {
                                                var firstPath = string.Join('\\', currentPath.GetRange(0, lvl + 1));
                                                block.Name = firstPath;
                                            }
                                            break;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                block.Name = Consts.DefaultBlockName + "\\" + element.NameBlock;
                            }
                        }

                        Value hyperlink =
                            new()
                            {
                                Name = $"",
                                Comment =
                                    $"@{element.NameBlock}@{Consts.SpecialDelimetr}{element.RusNameBlock}",
                            };

                        size.Length = hyperlink.Comment.Length * Consts.DefaultWidthChar;
                        size.Height = Consts.DefaultHeightText + 4;

                        Field hyperlinkText =
                            new()
                            {
                                FieldType = Type.Text,
                                FieldPosition = UpdateYCoordinate(
                                    Type.Text,
                                    positionBlock,
                                    ref mapCoordinates,
                                    0,
                                    updatedPath
                                ),
                                FieldSize = size,
                                FieldValue = hyperlink,
                            };

                        block.Fields.Add(hyperlinkText);

                        ListAllBlocks.Add(block);

                        if (!string.IsNullOrEmpty(block.Name))
                            NameValueCollectionPaths.Add(block.Name, block.ToString());
                        continue;
                    }

                    //Обработка остальных полей

                    previousLevel = UpdatePathAndReturnLevel(
                        ref currentPath,
                        previousLevel,
                        element
                    );

                    updatedPath = string.Join("\\", currentPath);
                    if (string.IsNullOrEmpty(element.RusNameBlock))
                    {
                        namesBlocks.TryAdd(updatedPath, "");
                    }
                    else
                    {
                        namesBlocks.TryAdd(updatedPath, element.RusNameBlock);
                    }


                    //Поиск полей шаблонов
                    bool filedInTemplate = searchFieldNames.Contains(
                        GetLastWord(element.FieldElement.ElementName.Split('_'))
                    );

                    //Поле (TEXT)
                    if (!filedInTemplate)
                    {
                        //проверим на точное совпадение
                        filedInTemplate = searchFieldNames.Contains(
                            element.FieldElement.ElementName
                        );

                        Value valueText = GetValueText(element);
                        if (!String.IsNullOrEmpty(valueText.Comment))
                        {
                            positionBlock = UpdateYCoordinate(
                                Type.Text,
                                positionBlock,
                                ref mapCoordinates,
                                0,
                                updatedPath
                            );
                            sizeBlock.Length =
                                valueText.Comment.Length * Consts.DefaultWidthChar;
                            sizeBlock.Height = Consts.DefaultHeightText;
                        }

                        Field fieldText =
                            new()
                            {
                                FieldType = Type.Text,
                                FieldPosition = positionBlock,
                                FieldSize = sizeBlock,
                                FieldValue = valueText,
                            };

                        //var color = Console.ForegroundColor;
                        //Console.ForegroundColor = ConsoleColor.Yellow;
                        //Console.WriteLine("{0} {1}", fieldText.FieldValue, field.FieldValue);
                        //Console.ForegroundColor = color;

                        block.Fields.Add(fieldText);

                        // Ищем признак что блок многострочный
                        if (seq)
                        {
                            sizeBlock.Height = Consts.DefaultHeightData * 2; //делаем двухстрочным
                        }
                        else
                        {
                            sizeBlock.Height = Consts.DefaultHeightData;
                        }

                        //(DATA)
                        //ищем длину поля и задаём длину блока
                        var posSpc = element.FieldElement.ElementValue.Trim().IndexOf(' ');
                        if (posSpc > 0)
                        {
                            var fidTyp = element.FieldElement.ElementValue.Remove(posSpc);
                            if (fidTyp.Contains('.'))
                            {
                                //удаляем дробную часть
                                fidTyp = fidTyp.Remove(fidTyp.IndexOf('.'));
                            }
                            fidTyp = String.Join("", fidTyp.Where(Char.IsDigit).ToList());
                            if (int.TryParse(fidTyp, out int fidLength))
                            {
                                if (posSpc == 1)
                                {
                                    //минимальный размер поля
                                    sizeBlock.Length = 16;
                                }
                                else
                                {
                                    sizeBlock.Length = fidLength * Consts.DefaultWidthFidChar;
                                }

                                if (sizeBlock.Length > Consts.DefaultWidthBlock)
                                {
                                    //обрезаем по ширине формы
                                    sizeBlock.Length = Consts.DefaultWidthBlock;
                                }
                            }
                        }
                        ;

                        positionBlock = UpdateYCoordinate(
                            Type.Data,
                            positionBlock,
                            ref mapCoordinates,
                            0,
                            updatedPath
                        );
                    }
                    else
                    {
                        //поле шаблона
                        SetElementCoordinate(
                            ref mapCoordinates,
                            updatedPath,
                            ref fidTemplate,
                            element,
                            ref sizeBlock,
                            ref positionBlock,
                            ref old_y,
                            ref textForTemplate
                        );

                        if (!string.IsNullOrEmpty(textForTemplate))
                        {
                            Value templateText = new() { Name = "", Comment = textForTemplate };
                            Size sizeTemplateText =
                                new(
                                    textForTemplate.Length * Consts.DefaultWidthChar,
                                    Consts.DefaultHeightText
                                );
                            Position positionTemplateText =
                                new(x: positionBlock.X, y: positionBlock.Y);

                            positionBlock.X =
                                GetXCoordinate(mapCoordinates)
                                + sizeTemplateText.Length
                                + Consts.DefaultWidthChar;

                            Field fieldTemplateText =
                                new()
                                {
                                    FieldType = Type.Text,
                                    FieldPosition = positionTemplateText,
                                    FieldSize = sizeTemplateText,
                                    FieldValue = templateText,
                                };
                            block.Fields.Add(fieldTemplateText);
                            textForTemplate = string.Empty;
                        }
                        sizeBlock.Height = Consts.DefaultHeightData;
                    }

                    Value valueData =
                        new(
                            name: element.FieldElement.ElementName,
                            comment: GetValueText(element.FieldElement.ElementValue)
                        );

                    Field field1 = new()
                    {
                        FieldType = Type.Data,
                        FieldPosition = positionBlock,
                        FieldSize = sizeBlock,
                        FieldValue = valueData,
                    };
                    Field field = field1;

                    previousLevel = UpdatePathAndReturnLevel(
                        ref currentPath,
                        previousLevel,
                        element
                    );

                    updatedPath = string.Join("\\", currentPath);

                    //Многострочноре поле (в dcf это параметра-> SEQ=)
                    if (seq)
                    {
                        UpdateYCoordinate(
                            Type.Data,
                            positionBlock,
                            ref mapCoordinates,
                            0,
                            updatedPath
                        );
                    }

                    if (string.IsNullOrEmpty(updatedPath))
                    {
                        //MAIN 
                        block.Name = element.NameBlock;
                    }
                    else
                    {
                        block.Name = updatedPath;
                    }
                    block.Fields.Add(field);
                    
                    NameValueCollectionPaths.Add(block.Name, block.ToString());                   

                    ListAllBlocks.Add(block);
                }

                var blkNameList = NameValueCollectionPaths.AllKeys.ToArray();

                var names = ListAllBlocks
                    .Where(block => !string.IsNullOrEmpty(block.Name))
                    .Select(block => block.Name)
                    .Distinct()
                    .ToArray();

                for (int i = 0; i < NameValueCollectionPaths.AllKeys.Count(); i++)
                {
                    if (string.IsNullOrEmpty(NameValueCollectionPaths[i]))
                    {
                        Console.WriteLine("Пустое значение ключа {0}", i);
                        continue;
                    }

                    DocumentLines.Add($"[{NameValueCollectionPaths.Keys[i].Replace("MAIN\\", "")}]");
                  
                    //string[] strArr = NameValueCollectionPaths[i].Split("\r\n");
                    string[] strArr = NameValueCollectionPaths[i].Split("\r\n").Select(str => str.TrimStart(',')).ToArray();
                    // Удаление последней пустой строки
                    if (strArr.Length > 0 && string.IsNullOrWhiteSpace(strArr[strArr.Length - 1]))
                    {
                        strArr = strArr.Take(strArr.Length - 1).ToArray();
                        Console.WriteLine("{0}", strArr);
                    }
                    //последняя строка нужна для вычисления размера блока
                    string lastRow = strArr[^1];
                    //вытаскиваем координаты из последней строки
                    string[] words = lastRow.Split(' '); // Data 0 255 32 16 .... etc
                    if (words.Length > 4)
                    {
                        if (int.TryParse(words[2], out int result))
                        {
                            DocumentLines.Add(
                                $"{Consts.DefaultWidthBlock}x"
                                    + (result + Consts.DefaultHeightData + 20).ToString()
                                    + $" 0 0 `{GetRussianNameFromCollection(namesBlocks, ((string)NameValueCollectionPaths.Keys[i]))}"
                            );
                        }
                        else
                        {
                            throw new Exception(
                                $"Ошибка преобразования {words[2]} в Integer. Немогу получить координаты блока из строки: \r\n"
                                    + $"{lastRow}"
                            );
                        }
                    }
                    else
                    {
                        Console.WriteLine($"{lastRow}");
                        throw new Exception($"Ошибка в строке: \r\n {lastRow}");
                    }
                    var rowsArr = NameValueCollectionPaths[i]
                        .ToString()
                        .TrimEnd()
                        .Replace($"@{Consts.SpecialDelimetr}", "@");
                    rowsArr = rowsArr.Replace(",\r\n", "\r\n");
                    rowsArr = rowsArr.Replace(",D", "D");
                    rowsArr = rowsArr.Replace(",T", "T");
                    DocumentLines.Add(rowsArr);
                   
                   
                    //конец блока                   
                    DocumentLines.Add("/");
                }

                DisplayDocument(DocumentLines);

                SaveToFile(DocumentName, DocumentLines);
                CreateSf2ListFile(DocumentName);
            }
            catch (System.NotSupportedException)
            {
                GetEncodingList();
                Console.WriteLine("Install System.Text.Encoding.CodePages...");
                Console.WriteLine("dotnet add package System.Text.Encoding.CodePages");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                throw;
            }

            static bool IsPreviousDataBlock(NameValueCollection NameValueCollectionPaths, Block block)
            {
                var lastString = NameValueCollectionPaths.Get(block.Name);
                if (lastString != null)
                {
                    //если последняя строка Data, то сделаем больший отступ
                    var daArr = lastString.Split("\r\n");
                    if (daArr.Length > 1)
                    {
                        if (string.IsNullOrEmpty(daArr[daArr.Length - 1]))
                        {
                            if (daArr[daArr.Length - 2].Contains("Data "))
                            {
                                return true;
                            }
                        }
                    }
                };
                return false;
            }
        }

        private static string? GetRussianNameFromCollection(
            Dictionary<string, string> collection,
            string key
        )
        {
            if (string.IsNullOrEmpty(key))
                return "";

            if (collection.TryGetValue(key, out var name))
                return name;
            else
                return "";
        }

        private static int UpdatePathAndReturnLevel(
            ref List<string> currentPath,
            int previousLevel,
            Data element
        )
        {
            if (element.Level == 0)
            {
                int indexOfMatchingElement = currentPath.FindIndex(x => x.EndsWith(element.NameBlock));
                if (indexOfMatchingElement != -1)
                {
                    currentPath = new List<string> { currentPath[indexOfMatchingElement] };
                    previousLevel = element.Level;
                    return previousLevel;
                }
            }
            else if (element.Level > previousLevel)
            {
                //не модет быть двух одинаковых?
                if (!currentPath.Last().EndsWith(element.NameBlock))
                {
                    currentPath.Add($"{element.NameBlock}");
                }
            }
            else if (element.Level < previousLevel)
            {
                var dif = previousLevel - element.Level;
                if (dif > 1)
                {
                    if (currentPath.Count > dif)
                    {
                        if (element.Level > currentPath.Count - 1)
                        {
                            //
                        }
                        else
                        {
                            try
                            {
                                //currentPath.RemoveRange(element.Level + 1, dif);
                                int startIndex = currentPath.IndexOf(element.NameBlock);
                                if (startIndex != -1)
                                {
                                    currentPath.RemoveRange(startIndex + 1, currentPath.Count - startIndex - 1);
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.ToString());
                                //throw;
                            }

                        }

                    }
                }
                else
                {
                    currentPath.RemoveRange(element.Level + 1, currentPath.Count - element.Level - 1);
                }

            }
            else
            {
                currentPath.RemoveRange(element.Level + 1, currentPath.Count - element.Level - 1);
                currentPath[^1] = $"{element.NameBlock}";
            }
            previousLevel = element.Level;
            return previousLevel;
        }

        private static bool SkipBlock(string comment)
        {
            var x = comment.Split(' ');
            StringCollection strings =
            [
                "российской федерации",
                "республики казахстан",
                "республики беларусь",
                "республики армения",
                "кыргызской республики",
            ];
            if (x.Length > 1)
            {
                var lastWords = x.TakeLast(2).First() + " " + x.Last();
                return strings.Contains(lastWords);
            }
            return false;
        }

        /******************************************************************************/
        private static void SetElementCoordinate(
            ref Dictionary<string, string> map,
            string currentPath,
            ref bool fidTemplate,
            Data item,
            ref Size sizeBlock,
            ref Position positionBlock,
            ref int oldY,
            ref string field
        )
        {
            string searchWord = item.FieldElement.ElementName;

            switch (searchWord)
            {
                case "Customs_Code":
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock);
                    sizeBlock.Length = 68;
                    fidTemplate = true;
                    return;
                case "Customs_OfficeName" when fidTemplate:
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 72;
                    sizeBlock.Length = 400;
                    fidTemplate = false;
                    return;
                default:
                    break;
            }

            //далее общие правила для шаблонов, смотрим по последнему слову.
            string[] elements = searchWord.Split('_');
            if (elements.Length > 0)
            {
                searchWord = elements[^1].Trim();
            }
            //Предполагается что порядок полей совпадает с dcf
            switch (searchWord)
            {
                //подпись
                case "PersonSurname":
                    positionBlock.Y = IncreaseYCoordinate(positionBlock, map, currentPath).Y;
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock);
                    sizeBlock.Length = 160;
                    fidTemplate = true;
                    break;
                case "PersonName" when fidTemplate:
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 164;
                    sizeBlock.Length = 156;
                    break;
                case "PersonMiddleName" when fidTemplate:
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 324;
                    sizeBlock.Length = 160;
                    break;
                case "PersonPost" when fidTemplate:
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 488;
                    sizeBlock.Length = 188;
                    break;
                case "IssueDate" when fidTemplate:
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 680;
                    sizeBlock.Length = 188;
                    fidTemplate = false;
                    break;
                //блок номера ДТ
                case "CustomsCode":
                    positionBlock.Y = IncreaseYCoordinate(positionBlock, map, currentPath).Y;
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock);
                    sizeBlock.Length = 72;
                    fidTemplate = true;
                    break;
                case "RegistrationDate" when fidTemplate:
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 74;
                    sizeBlock.Length = 88;
                    break;
                case "GTDNumber" when fidTemplate:
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 168;
                    sizeBlock.Length = 60;
                    fidTemplate = false;
                    break;
                //документ
                case "CustomsDocumentCode":
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = Consts.Default_X;
                    sizeBlock.Length = 44;
                    break;
                case "PrDocumentName":
                    positionBlock.Y = IncreaseYCoordinate(positionBlock, map, currentPath).Y;
                    if (GetXCoordinate(map, nameBlock: item.NameBlock) > Consts.Default_X)
                    {
                        positionBlock.X = 52;
                    }
                    else
                    {
                        positionBlock.X = Consts.Default_X;
                    }
                    sizeBlock.Length = 334;
                    fidTemplate = true;
                    break;
                case "PrDocumentNumber" when fidTemplate:
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    if (GetXCoordinate(map, nameBlock: item.NameBlock) > Consts.Default_X + 334)
                    {
                        positionBlock.X = 390;
                    }
                    else
                    {
                        positionBlock.X = Consts.Default_X + 338;
                    }
                    sizeBlock.Length = 300;
                    break;
                case "PrDocumentDate" when fidTemplate:
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    if (GetXCoordinate(map, nameBlock: item.NameBlock) > Consts.Default_X + 390)
                    {
                        positionBlock.X = 694;
                    }
                    else
                    {
                        positionBlock.X = Consts.Default_X + 642;
                    }
                    sizeBlock.Length = 80;
                    fidTemplate = false;
                    break;
                //единицы измер.
                case "GoodsQuantity":
                    positionBlock.Y = IncreaseYCoordinate(positionBlock, map, currentPath).Y;
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock);
                    sizeBlock.Length = 192;
                    fidTemplate = true;
                    break;
                case "MeasureUnitQualifierName" when fidTemplate:
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 196;
                    sizeBlock.Length = 284;
                    break;
                case "MeasureUnitQualifierCode" when fidTemplate:
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 484;
                    sizeBlock.Length = 32;
                    fidTemplate = false;
                    break;
                //Адрес.
                case "AddressKindCode":
                    positionBlock.Y = IncreaseYCoordinate(positionBlock, map, currentPath).Y;
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock);
                    sizeBlock.Length = 20;
                    fidTemplate = true;
                    break;
                case "PostalCode" when fidTemplate:
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 320;
                    sizeBlock.Length = 100;
                    break;
                case "CountryCode" when fidTemplate:
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 24;
                    sizeBlock.Length = 28;
                    break;
                case "CounryName" when fidTemplate:
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 56;
                    sizeBlock.Length = 260;
                    break;
                case "Region" when fidTemplate:
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 424;
                    sizeBlock.Length = 348;
                    break;
                case "District" when fidTemplate:
                    positionBlock.Y = IncreaseYCoordinate(positionBlock, map, currentPath).Y;
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock);
                    sizeBlock.Length = 316;
                    break;
                case "Town" when fidTemplate:
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 320;
                    sizeBlock.Length = 208;
                    break;
                case "City" when fidTemplate:
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 532;
                    sizeBlock.Length = 240;
                    break;
                case "StreetHouse" when fidTemplate:
                    positionBlock.Y = IncreaseYCoordinate(positionBlock, map, currentPath).Y;
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock);
                    sizeBlock.Length = 528;
                    break;
                case "House" when fidTemplate:
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 532;
                    sizeBlock.Length = 112;
                    break;
                case "Room" when fidTemplate:
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 648;
                    sizeBlock.Length = 76;
                    oldY = positionBlock.Y;
                    break;
                case "AddressText" when fidTemplate:
                    positionBlock.Y = IncreaseYCoordinate(positionBlock, map, currentPath).Y;
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock);
                    sizeBlock.Length = 772;
                    break;
                case "PostOfficeBoxId" when fidTemplate:
                    positionBlock.Y = oldY;
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 728;
                    sizeBlock.Length = 44;
                    fidTemplate = false;
                    oldY = 0;
                    break;

                //Сведения об организации.
                case "OrganizationName":
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock);
                    sizeBlock.Length = 748;
                    break;
                case "OrganizationLanguage":
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 752;
                    sizeBlock.Length = 20;
                    break;
                case "ShortName":
                    positionBlock.Y = IncreaseYCoordinate(positionBlock, map, currentPath).Y;
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock);
                    sizeBlock.Length = 316;
                    break;
                case "OGRN":
                    field = "ОГРН";
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock);
                    EditElementDescription(item, field);
                    sizeBlock.Length = 120;
                    break;
                case "INN" when fidTemplate:
                    field = "ИНН";
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 124;
                    EditElementDescription(item, field);
                    sizeBlock.Length = 96;
                    break;
                case "KPP" when fidTemplate:
                    field = "КПП";
                    positionBlock.Y = GetYCoordinate(map, currentPath);
                    positionBlock.X = GetXCoordinate(map, nameBlock: item.NameBlock) + 224;
                    EditElementDescription(item, field);
                    sizeBlock.Length = 72;
                    break;
                default:
                    //
                    break;
            }

            static void EditElementDescription(Data item, string description)
            {
                if (item.FieldElement.ElementValue.Contains('|'))
                {
                    var value = item.FieldElement.ElementValue.Substring(
                        0,
                        item.FieldElement.ElementValue.IndexOf('|')
                    );
                    item.FieldElement.ElementValue = value + "|" + description;
                }
                ;
            }
        }

        /// <summary>
        /// The function IsServiceField checks if a given element name corresponds to a service field.
        /// </summary>
        /// <param name="elementName">The `IsServiceField` method is checking if a given `elementName`
        /// corresponds to a service field. To provide more specific guidance, could you please clarify
        /// what type of service field you are referring to?</param>
        private static bool IsServiceField(string elementName)
        {
            if (elementName.Equals("MCD_ID", StringComparison.OrdinalIgnoreCase))
                return true;
            // общие правила
            string[] skipWord =
            [
                "Fax",
                "Telex",
                "DocumentID",
                "RefDocumentID",
                "INNSign",
                "OKTMO",
                "OKATO",
                "KLADR",
                "AOGUID",
                "AOID",
                "TerritoryCode",
                "ITNReserv",
            ];
            string[] elements = elementName.Split('_');
            string lastWord = GetLastWord(elements);
            return skipWord.Contains(lastWord);
        }

        private static string GetLastWord(string[] elements)
        {
            string lastWord = "";
            if (elements.Length > 0)
            {
                lastWord = elements[^1].Trim();
            }

            return lastWord;
        }

        /// <summary>
        /// Проверка на начало информационного блока. Запись вида: PropertyLocation=B |* Местонахождение объекта недвижимости NAMESPACE="PropertyLocation"
        /// </summary>
        /// <param name="item"></param>
        /// <returns>boolean</returns>
        private static bool IsBlockField(Data item)
        {
            return item.FieldElement.ElementValue.Trim().Contains("B |");
        }

        private static void SaveToFile(string fileName, List<string> documentLines)
        {
            string path = $"D:\\Alta\\data\\ed\\5_24_0\\{fileName}.sf2";
            using StreamWriter sw = new(path, false, Encoding.GetEncoding(1251));
            foreach (string item in documentLines)
            {
                sw.WriteLine(item);
            }
            Console.ForegroundColor = ConsoleColor.Green;

            //Console.BackgroundColor = ConsoleColor.Blue;
            Console.WriteLine("Файл записан:" + path);
            Console.ResetColor();

        }

        private static void CreateSf2ListFile(string fileName)
        {
            string firstRow = $"DOCUMENT=" + fileName;
            string path = $"D:\\Alta\\data\\ed\\5_24_0\\{fileName}List.sf2";
            if (!File.Exists(path))
            {
                using StreamWriter sw = new(path, false, Encoding.GetEncoding(1251));
                sw.WriteLine(
                    firstRow
                        + "\r\nList=@FileDate,Дата и время,18,T\r\nList=DocumentNumber,Номер,30,7\r\nList=@Binded,Связанные,10\r\n[MAIN]\r\n780x160 0 0 `\r\n/"
                );
            }
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Файл List записан:" + path);
            Console.ResetColor();
        }

        /// <summary>
        /// Получение комментария к полю.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        private static Value GetValueText(Data item)
        {
            string comment = item.FieldElement.ElementValue;
            if (comment.Contains('|'))
            {
                //удалим обозначение типа поля dcf, до комментария
                comment = item.FieldElement.ElementValue.Remove(
                    0,
                    item.FieldElement.ElementValue.IndexOf('|') + 1
                );
                comment = RemoveSpecialFieldSpecialChars(comment);
            }
            else
            {
                comment = string.Empty;
            }
            Value valueText = new(name: string.Empty, comment: comment);
            return valueText;
        }

        private static string GetValueText(string comment)
        {
            if (comment.Contains('|'))
            {
                //удалим обозначение типа поля dcf, до комментария
                comment = comment.Remove(0, comment.IndexOf('|') + 1);
                comment = RemoveSpecialFieldSpecialChars(comment);
            }
            else
            {
                comment = string.Empty;
            }
            return comment;
        }

        private static string RemoveSpecialFieldSpecialChars(string comment)
        {
            if (comment.StartsWith('!') || comment.StartsWith('*'))
            {
                comment = comment.Remove(0, 1).Trim();
            }
            if (comment.IndexOf("SEQ=") > 0)
            {
                comment = comment.Remove(comment.IndexOf("SEQ="), 7); //удалим SEQ="1"
            }

            return comment;
        }

        private static Position UpdateYCoordinate(
            Type type,
            Position position,
            ref Dictionary<string, string> map,
            int bold = 0,
            string nameBlock = Consts.DefaultBlockName
        )
        {
            Position retPos = new(x: Consts.Default_X, y: Consts.Default_Y);

            bool isText = type == Type.Text;

            if (!map.TryAdd(nameBlock, $"{position.X} {position.Y} {isText}"))
            {
                string[] tt;
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                tt = map.GetValueOrDefault(nameBlock).Split(' ');
#pragma warning restore CS8602 // Dereference of a possibly null reference.

                if (int.TryParse(tt[1], out int y))
                {
                    if (isText)
                    {
                        if (tt[2].Equals("True"))
                        {
                            y = y + Consts.DefaultHeightText + bold;
                        }
                        else
                        {
                            y =
                                y
                                + Consts.DefaultHeightText
                                + bold
                                + Consts.DefaultHeightData
                                - (Consts.DefaultHeightData - Consts.DefaultHeightText);
                            tt[2] = isText.ToString();
                        }
                    }
                    else
                    {
                        y =
                            y
                            + Consts.DefaultHeightData
                            - (Consts.DefaultHeightData - Consts.DefaultHeightText);
                        tt[2] = isText.ToString();
                    }
                    map[nameBlock] = tt[0] + ' ' + y.ToString() + ' ' + tt[2];
                    retPos.Y = y;
                    if (int.TryParse(tt[0], out int x))
                    {
                        retPos.X = x;
                    }
                    else
                    {
                        retPos.X = Consts.Default_X;
                    }
                }
            }
            return retPos;
        }

        private static Position IncreaseYCoordinate(
            Position position,
            Dictionary<string, string> map,
            string nameBlock = Consts.DefaultBlockName
        )
        {
            Position retPos = new(x: Consts.Default_X, y: Consts.Default_Y);
            if (!map.TryAdd(nameBlock, $"{position.X} {position.Y} False"))
            {
                string[] tt;
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                tt = map.GetValueOrDefault(nameBlock).Split(' ');
#pragma warning restore CS8602 // Dereference of a possibly null reference.
                if (int.TryParse(tt[1], out int y))
                {
                    retPos.X = position.X;
                    y = y + Consts.DefaultHeightData + 4;
                    retPos.Y = y;
                    map[nameBlock] = $"{retPos.X} {retPos.Y} False";
                }
            }
            return retPos;
        }

        private static Position IncreaseXCoordinate(
            Position position,
            int length,
            Dictionary<string, string> map,
            string nameBlock = Consts.DefaultBlockName
        )
        {
            Position retPos = new(x: Consts.Default_X, y: Consts.Default_Y);
            if (!map.TryAdd(nameBlock, $"{position.X} {position.Y} False"))
            {
                string[] tt;
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                tt = map.GetValueOrDefault(nameBlock).Split(' ');
#pragma warning restore CS8602 // Dereference of a possibly null reference.
                if (int.TryParse(tt[0], out int x))
                {
                    x += length;
                    map[nameBlock] = $"{x} {tt[1]} {tt[2]}";
                    retPos.X = x;
                    if (int.TryParse(tt[1], out int y))
                    {
                        retPos.Y = y;
                    }
                    else
                    {
                        retPos.Y = 0;
                    }
                }
            }
            return retPos;
        }

        private static int GetYCoordinate(
            Dictionary<string, string> map,
            string nameBlock = Consts.DefaultBlockName
        )
        {
            if (!map.TryAdd(nameBlock, $"{Consts.Default_X} {Consts.Default_Y} False"))
            {
                string[] tt;
#pragma warning disable CS8602 // Dereference of a possibly null reference.p+7. v
                tt = map.GetValueOrDefault(nameBlock).Split(' ');
#pragma warning restore CS8602 // Dereference of a possibly null reference.
                if (int.TryParse(tt[1], out int y))
                {
                    return y;
                }
            }
            return Consts.Default_Y;
        }

        private static int GetXCoordinate(
            Dictionary<string, string> map,
            string nameBlock = Consts.DefaultBlockName
        )
        {
            if (!map.TryAdd(nameBlock, $"{Consts.Default_X} {Consts.Default_Y} False"))
            {
                string[] tt;
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                tt = map.GetValueOrDefault(nameBlock).Split(' ');
#pragma warning restore CS8602 // Dereference of a possibly null reference.
                if (int.TryParse(tt[0], out int x))
                {
                    return x;
                }
            }
            return Consts.Default_X;
        }

        private static string ReadFile(FileInfo file)
        {
            byte[] fileBytes = File.ReadAllBytes(file.FullName);
            var str = Encoding.GetEncoding("windows-1251").GetString(fileBytes);
            return str;
        }

        private static void DisplayDocument(List<string> documentLines)
        {
            foreach (var item in documentLines)
            {
                Console.WriteLine(item);
            }
        }

        /// <summary>
        /// This function converts a text file into a list of Data objects.
        /// </summary>
        /// <param name="text">The `ConvertFileToData` method takes a string `text` as input, which
        /// presumably contains data that needs to be converted into a list of `Data` objects.</param>
        private static List<Data> ConvertFileToData(string text)
        {
            string wordToFind = ".Fields]";

            Match match = Regex.Match(text, wordToFind);

            if (match.Success)
            {
                int lastIndex = match.Index + wordToFind.Length;
                text = text[lastIndex..];
            }
            else
            {
                throw new Exception("Не удалось найти блок Fields");
            }

            //string fieldsSection = GetFieldsSection(text);
            var collection = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Select(s => s.TrimStart()).ToList();

            List<Data> Elements = [];

            collection = RemovePattrens(collection);

            int i = 0,
                level = 0;
            string rusName = string.Empty;
            Stack<string> stackBlock = new();

            foreach (var item in collection)
            {
                i++;
                if (item is not null)
                {
                    if (item.Trim().StartsWith(';'))
                    {
                        //комментарий
                        rusName = item.Replace("; ====> ", "").Trim();
                        continue;
                    }
                    if (item.IndexOfAny(['=']) != -1)
                    {
                        var name = item.Trim().Substring(0, item.IndexOfAny(['='])).ToString();
                        Element element =
                            new()
                            {
                                ElementName = name,
                                ElementValue = item.Replace(name + "=", ""),
                            };

                        string nam = Consts.DefaultBlockName;
                        if (stackBlock.TryPeek(out var nameBlock))
                        {
                            nam = nameBlock;
                        }
                        ;

                        Data data =
                            new()
                            {
                                NameBlock = nam,
                                RusNameBlock = rusName,
                                Level = level,
                                FieldElement = element,
                            };

                        Elements.Add(data);
                        continue;
                    }
                    else
                    {
                        if (item.IndexOfAny(['{']) != -1)
                        {
                            level++;
                            var el = item.Remove(item.IndexOfAny(['{']));
                            Element element = new() { ElementName = el, ElementValue = "" };

                            stackBlock.Push(element.ElementName);

                            Data data =
                                new()
                                {
                                    Level = level,
                                    RusNameBlock = rusName,
                                    NameBlock = element.ElementName,
                                    FieldElement = element,
                                };
                            Elements.Add(data);

                            continue;
                        }
                        else
                        {
                            //Console.WriteLine($"{item}, {level}");
                        }
                        if (item.Trim().StartsWith('}'))
                        {
                            if (level > 0)
                            {
                                level--;
                                stackBlock.Pop();
                            }
                        }
                    }
                }
            }
            return Elements;

            static List<string> RemovePattrens(List<string> collection)
            {
                for (int i = 0; i < collection.Count; i++)
                {
                    if (collection[i].Contains("PATTERN="))
                    {
                        Regex regex = new("PATTERN=");
                        Match match = regex.Match(collection[i]);
                        int startIndex = match.Index;
                        collection[i] = collection[i].Remove(startIndex);
                    }

                    if (collection[i].Contains("NAMESPACE="))
                    {
                        Regex regex = new("NAMESPACE=");
                        Match match = regex.Match(collection[i]);
                        int startIndex = match.Index;
                        collection[i] = collection[i].Remove(startIndex);
                    }
                }

                return collection;
            }
        }

        private static string GetDocumentName(string text)
        {
            if (String.IsNullOrEmpty(text))
            {
                return "";
            }

            Regex rgx = new("\\[[A-Za-z0-9]+.*\\]"); // new Regex("[^\\]]*");

            var fields = rgx.Match(text).Value;
            if (fields.Length > 9)
            {
                fields = fields[1..^8]; // remove ".fields" pattern
            }

            return fields;
        }

        private static string GetFieldsSection(string text)
        {
            if (String.IsNullOrEmpty(text))
            {
                return "";
            }
            Regex rgx = new("\\[[A-Za-z0-9]+.*\\]");
            var fields = rgx.Match(text).Value;
            var index = text.IndexOf(fields, StringComparison.Ordinal);
            return text.Substring(index, text.Length - index);
        }

        public static void GetEncodingList()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            // Get the list of available character encodings
            EncodingInfo[] encodings = Encoding.GetEncodings();

            // Output the list of encodings
            foreach (EncodingInfo encodingInfo in encodings)
            {
                Console.WriteLine($"{encodingInfo.CodePage}: {encodingInfo.Name}");
            }
        }

        static List<Data> FindSequentialData(List<Data> dataList, List<string> searchFieldNames)
        {
            List<Data> result = [];

            for (int i = 0; i < dataList.Count - searchFieldNames.Count + 1; i++)
            {
                bool foundSequence = true;

                for (int j = 0; j < searchFieldNames.Count; j++)
                {
                    if (dataList[i + j].FieldElement.ElementName != searchFieldNames[j])
                    {
                        foundSequence = false;
                        break;
                    }
                }

                if (foundSequence)
                {
                    for (int j = 0; j < searchFieldNames.Count; j++)
                    {
                        result.Add(dataList[i + j]);
                    }
                }
            }

            return result;
        }
    }
}
