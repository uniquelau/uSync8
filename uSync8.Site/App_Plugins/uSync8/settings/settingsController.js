(function () {
    'use strict';

    function settingsController($scope,
        editorService,
        uSync8DashboardService,
        notificationsService) {

        var vm = this;
        vm.working = false; 
        vm.loading = true; 

        vm.umbracoVersion = Umbraco.Sys.ServerVariables.application.version;

        vm.saveSettings = saveSettings;
        vm.toggleSetting = toggleSetting;

        vm.openHandlerConfig = openHandlerConfig;

        init();

        ///////////

        function init() {
            getSettings();
        }

        ///////////
        function getSettings() {

            uSync8DashboardService.getSettings()
                .then(function (result) {
                    vm.settings = result.data;
                    vm.loading = false;


                    vm.settings.HandlerSets.forEach(function (set) {
                        console.log(set, vm.settings.DefaultSet);
                        if (set.Name == vm.settings.DefaultSet) {
                            set.show = true;
                            console.log('show');
                        }
                    })
                });
        }

        // toggles a value, and updates the value in all the handlers (where its not overridden)
        function toggleSetting(propertyName, handlerProperty) {

            if (vm.settings[propertyName] !== undefined) {
                vm.settings[propertyName] = !vm.settings[propertyName];

                if (handlerProperty === undefined) {
                    handlerProperty = propertyName;
                }

                vm.settings.HandlerSets.forEach(function (set) {
                    set.Handlers.forEach(function (handler) {
                        // find the value by name in the handler object?
                        if (handler[handlerProperty] !== undefined && handler[handlerProperty].IsOverridden == false) {
                            handler[handlerProperty].Value = vm.settings[propertyName];
                        }
                    });
                });
            }
        }
        

        function saveSettings() {
            vm.working = false;
            uSync8DashboardService.saveSettings(vm.settings)
                .then(function (result) {
                    vm.working = false;
                    notificationsService.success('Saved', 'Settings updated');
                }, function (error) {
                    notificationsService.error('Saving', error.data.Message);
                });
        }

        
        function openHandlerConfig(config) {

            editorService.open({
                config: config,
                default: vm.settings,
                title: 'handler config',
                size: 'small',
                view: Umbraco.Sys.ServerVariables.umbracoSettings.appPluginsPath + '/uSync8/settings/handlerConfig.html',
                submit: function (done) {
                    editorService.close();
                },
                close: function () {
                    editorService.close();
                    console.log(config);
                }
            });

        }

    }

    angular.module('umbraco')
        .controller('uSyncSettingsController', settingsController);
})();