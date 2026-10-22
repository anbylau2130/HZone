// cherry drawIO 桥接脚本（照抄官方 examples/assets/scripts/drawio-demo.js，仅注释补充）：
// 实现 cherry drawIo 弹层的私有 postMessage 协议——
//   cherry → 本页：setData（载入 xml）/ getData（导出 PNG+xml）/ ready?（探活）
//   本页 → cherry：ready（EditorUi 就绪）/ setData:success / getData:success {xmlData, base64}
// base64 为完整 dataURL（createImageDataUri，editable=true 时 xml 已嵌 PNG zTXt 块），
// cherry 原生把它以 ![..](data:image/png;base64,..){data-type=drawio data-xml=..} 插入正文，
// 该形态同时是预览区"点击图片重新编辑"的匹配目标（cherry 内部 tQ 正则），勿改成 /media 路径。
// Extends EditorUi to update I/O action states based on availability of backend
(function () {
  var editorUiInit = EditorUi.prototype.init;

  EditorUi.prototype.init = function () {
    editorUiInit.apply(this, arguments);
  };

  // Adds required resources (disables loading of fallback properties, this can only
  // be used if we know that all keys are defined in the language specific file)
  mxResources.loadDefaultBundle = false;
  var bundle = './drawio_lib/resources/' + mxLanguage + '.txt';

  // Fixes possible asynchronous requests
  mxUtils.getAll([bundle, './drawio_lib/theme/default.xml'], function (xhr) {
    // Adds bundle text to resources
    mxResources.parse(xhr[0].getText());

    // Configures the default graph theme
    var themes = new Object();
    themes[Graph.prototype.defaultThemeName] = xhr[1].getDocumentElement();

    // Main
    window.editorUIInstance = new EditorUi(new Editor(false, themes));

    try {
      addPostMessageListener(editorUIInstance.editor);
    } catch (error) {
      console.log(error);
    }
    window.parent.postMessage({ eventName: 'ready', value: '' }, '*');
  }, function () {
    document.body.innerHTML =
      '<center style="margin-top:10%;">Error loading resource files. Please check browser console.</center>';
  });
})();

function addPostMessageListener(graphEditor) {
  window.addEventListener('message', function (event) {
    if (!event.data || !event.data.eventName) {
      return;
    }
    switch (event.data.eventName) {
      case 'setData': {
        var value = event.data.value || '';
        var documentName = 'cherry-drawio-' + new Date().getTime();
        // 空串防护：cherry 新建画图时 setData 传空 xml，而 Chromium 的
        // DOMParser.parseFromString('', 'text/xml') 会返回根为 <html> 的兜底文档，
        // 直接 importGraphModel 会触发 mxCodec allowlist 报错（html not allowed）。
        // 只接受真正的 mxGraphModel 根，其余（空串/解析失败）按空画布处理。
        if (value.replace(/\s+/g, '') !== '') {
          var doc = mxUtils.parseXml(value);
          if (doc && doc.documentElement && doc.documentElement.nodeName === 'mxGraphModel') {
            editorUIInstance.editor.setGraphXml(null);
            graphEditor.graph.importGraphModel(doc.documentElement);
          }
        }
        graphEditor.setFilename(documentName);
        window.parent.postMessage({ eventName: 'setData:success', value: '' }, '*');
        break;
      }
      case 'getData':
        editorUIInstance.editor.graph.stopEditing();
        var xmlData = mxUtils.getXml(editorUIInstance.editor.getGraphXml());
        // exportImage(scale=1, bg=#ffffff, ignoreSelection=true, shadow=null,
        // editable=true(把 xml 嵌进 PNG 块), border=50, noCrop=null, format=png, cb)
        editorUIInstance.exportImage(1, '#ffffff', true, null, true, 50, null, 'png', function (base64, filename) {
          window.parent.postMessage({
            mceAction: 'getData:success',
            eventName: 'getData:success',
            value: {
              xmlData: xmlData,
              base64: base64,
            },
          }, '*');
        });
        break;
      case 'ready?':
        window.parent.postMessage({ eventName: 'ready', value: '' }, '*');
        break;
      default:
        break;
    }
  });
}
