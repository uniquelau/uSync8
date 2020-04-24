(function () {
    'use strict';

    function handlerConfigController($scope) {

        var dvm = this;


        dvm.toggleOverride = toggleOverride;

        dvm.selection = [];
        dvm.actions = [
            { name: "Report", value: "Report" },
            { name: "Import", value: "Import" },
            { name: "Export", value: "Export" },
            { name: "Save", value: "Save" }
        ];

        dvm.actions.forEach(function (action) {
            if ($scope.model.config.Actions.indexOf(action.value) !== -1
            || $scope.model.config.Actions.indexOf("All") !== -1) {
                action.selected = true;
            }
        })

        $scope.$watch('dvm.actions|filter:{selected:true}', function (nv) {

            if (nv !== undefined) {
                $scope.model.config.Actions = nv.map(function (action) {
                    return action.value;
                });

                if ($scope.model.config.Actions.length == dvm.actions.length) {
                    $scope.model.config.Actions = ["All"];
                }
            }
        }, true);

        function toggleOverride(item, defaultValue) {
            item.Value = !item.Value;
            item.IsOverridden = (item.Value !== defaultValue);
        }

    }


    angular.module('umbraco')
        .controller('uSyncHandlerConfigController', handlerConfigController);
})();