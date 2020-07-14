# ImageCollectionTool

Tool for helping organize collections of images.
2 Functions:

> Checking if images follow naming pattern: 'Name_1', 'Name_2', etc...
>> 'Name_#a', Name_#b', etc... allowed up to 'z'.

> Checking if there are duplicate images by comparing the Hue-Saturation-Brightness average of each image. 
>>This method is not ideal, but works well enough. Currently images are marked as duplicates if their HSB values are 99.97% similar.
